using System.Linq;
using System.Collections;
using UnityEngine;
using Ubiq.Rooms;

/// <summary>
/// Attiva/disattiva le Workstation e i Virtual_Whiteboard in base
/// al numero di utenti connessi alla stanza Ubiq (1-4).
/// </summary>
public class WorkstationManager : MonoBehaviour
{
    [Header("Workstations (index 0 = Workstation1, index 3 = Workstation4)")]
    [SerializeField] private GameObject[] workstations = new GameObject[4];

    // Chiave della peer property che memorizza il numero di slot (1-4)
    private const string SLOT_KEY = "wm_slot";

    private RoomClient roomClient;

    private int  lastActiveCount       = -1;
    private int  lastLocalPlayerNumber = -1;
    private int  localPlayerNumber     =  0;   // 0 = non ancora assegnato
    private bool _claimingSlot         = false; // guard anti-coroutine-duplicata

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        roomClient = RoomClient.Find(this);

        if (roomClient == null)
        {
            Debug.LogError("[WorkstationManager] RoomClient non trovato!");
            return;
        }

        roomClient.OnJoinedRoom.AddListener(OnJoinedRoom);
        roomClient.OnPeerAdded.AddListener(OnPeerAdded);
        roomClient.OnPeerRemoved.AddListener(OnPeerRemoved);
        roomClient.OnRoomUpdated.AddListener(OnRoomUpdated);

        RefreshVisibility();
    }

    private void OnDestroy()
    {
        if (roomClient == null) return;
        roomClient.OnJoinedRoom.RemoveListener(OnJoinedRoom);
        roomClient.OnPeerAdded.RemoveListener(OnPeerAdded);
        roomClient.OnPeerRemoved.RemoveListener(OnPeerRemoved);
        roomClient.OnRoomUpdated.RemoveListener(OnRoomUpdated);
    }

    // -------------------------------------------------------------------------
    // Ubiq callbacks
    // -------------------------------------------------------------------------

    private void OnJoinedRoom(IRoom room)
    {
        // Reset: sarà riassegnato da ClaimSlotCoroutine
        localPlayerNumber     = 0;
        lastLocalPlayerNumber = -1;
        _claimingSlot         = false;
        RefreshVisibility();
    }

    private void OnPeerAdded(IPeer peer)
    {
        // Se non abbiamo ancora uno slot, prova ad assegnarne uno ora
        // (utile se il primo OnRoomUpdated è arrivato troppo presto)
        if (localPlayerNumber == 0)
            TryClaimSlot();

        RefreshVisibility();
    }

    private void OnPeerRemoved(IPeer peer) => RefreshVisibility();

    private void OnRoomUpdated(IRoom room)
    {
        if (localPlayerNumber == 0)
            TryClaimSlot();

        RefreshVisibility();
    }

    // -------------------------------------------------------------------------
    // Slot assignment
    // -------------------------------------------------------------------------

    /// <summary>
    /// Punto di ingresso pubblico: avvia la coroutine solo se non è già in corso.
    /// </summary>
    private void TryClaimSlot()
    {
        if (_claimingSlot || localPlayerNumber != 0) return;
        _claimingSlot = true;
        StartCoroutine(ClaimSlotCoroutine());
    }

    /// <summary>
    /// Assegna lo slot con un breve delay per permettere alle peer property
    /// dei peer già connessi di sincronizzarsi prima della lettura.
    /// Usa le PEER property (roomClient.Me["wm_slot"]) invece delle room
    /// property: le peer property arrivano atomicamente con lo stato del peer,
    /// eliminando la race condition che assegnava slot 1 a tutti.
    /// </summary>
    private IEnumerator ClaimSlotCoroutine()
    {
        // Lascia propagare le peer property dei peer già presenti
        yield return new WaitForSeconds(0.5f);

        // Se nel frattempo lo slot è già stato assegnato (es. doppia chiamata), esci
        if (localPlayerNumber != 0)
        {
            _claimingSlot = false;
            yield break;
        }

        // Caso reconnect: la nostra peer property potrebbe essere già valorizzata
        if (roomClient?.Me != null)
        {
            string existing = roomClient.Me[SLOT_KEY];
            if (!string.IsNullOrEmpty(existing) &&
                int.TryParse(existing, out int n) &&
                n >= 1 && n <= 4)
            {
                localPlayerNumber = n;
                lastLocalPlayerNumber = -1;
                _claimingSlot = false;
                Debug.Log($"[WorkstationManager] Slot ripristinato dalla peer property: Player {localPlayerNumber}");
                RefreshVisibility();
                yield break;
            }
        }

        // Leggi gli slot già occupati dalle peer property dei peer connessi
        bool[] occupied = new bool[5]; // indici 1-4

        foreach (var peer in roomClient.Peers)
        {
            string val = peer[SLOT_KEY];
            if (!string.IsNullOrEmpty(val) &&
                int.TryParse(val, out int taken) &&
                taken >= 1 && taken <= 4)
            {
                occupied[taken] = true;
                Debug.Log($"[WorkstationManager] Peer {peer.uuid} occupa slot {taken}");
            }
        }

        // Prendi il primo slot libero e scrivilo nella nostra peer property
        for (int slot = 1; slot <= 4; slot++)
        {
            if (!occupied[slot])
            {
                localPlayerNumber = slot;
                roomClient.Me[SLOT_KEY] = slot.ToString();
                lastLocalPlayerNumber = -1;
                _claimingSlot = false;
                Debug.Log($"[WorkstationManager] Slot assegnato: Player {localPlayerNumber}");
                RefreshVisibility();
                yield break;
            }
        }

        // Tutti gli slot 1-4 sono occupati (caso >4 utenti o sincronizzazione lenta):
        // rilascia il guard e riprova dopo un po'
        _claimingSlot = false;
        Debug.LogWarning("[WorkstationManager] Nessuno slot libero, riprovo tra 2s...");
        yield return new WaitForSeconds(2f);
        TryClaimSlot();
    }

    // -------------------------------------------------------------------------
    // Core logic
    // -------------------------------------------------------------------------

    private void RefreshVisibility()
    {
        if (roomClient == null) return;

        int activeCount = Mathf.Clamp(roomClient.Peers.Count() + 1, 1, 4);

        bool slotPending = (localPlayerNumber == 0);
        if (!slotPending &&
            activeCount == lastActiveCount &&
            localPlayerNumber == lastLocalPlayerNumber)
            return;

        lastActiveCount       = activeCount;
        lastLocalPlayerNumber = localPlayerNumber;

        Debug.Log($"[WorkstationManager] Refresh → Utenti: {activeCount} | LocalPlayer: {localPlayerNumber}");

        for (int i = 0; i < workstations.Length; i++)
        {
            if (workstations[i] == null)
            {
                Debug.LogWarning($"[WorkstationManager] workstations[{i}] è null!");
                continue;
            }

            int  workstationNumber = i + 1;
            bool shouldBeActive    = workstationNumber <= activeCount;

            workstations[i].SetActive(shouldBeActive);

            if (shouldBeActive)
                UpdateVirtualWhiteboards(workstations[i], workstationNumber, activeCount);
        }
    }

    /// <summary>
    /// Mostra i Virtual_Whiteboard_j sulla workstation locale (quella il cui numero
    /// corrisponde a localPlayerNumber) e li nasconde su tutte le altre.
    /// </summary>
    private void UpdateVirtualWhiteboards(GameObject workstation,
                                          int ownWorkstationNumber,
                                          int activeCount)
    {
        bool isMyWorkstation = (localPlayerNumber != 0 &&
                                ownWorkstationNumber == localPlayerNumber);

        for (int j = 1; j <= 4; j++)
        {
            if (j == ownWorkstationNumber) continue;

            string    vwName      = $"Virtual_Whiteboard_{j}";
            Transform vwTransform = workstation.transform.Find(vwName)
                                 ?? FindDeepChild(workstation.transform, vwName);

            if (vwTransform == null)
            {
                if (j <= activeCount)
                    Debug.LogWarning($"[WorkstationManager] '{vwName}' non trovato " +
                                     $"in Workstation{ownWorkstationNumber}.");
                continue;
            }

            bool show = isMyWorkstation && j <= activeCount;
            vwTransform.gameObject.SetActive(show);
        }
    }

    private Transform FindDeepChild(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform found = FindDeepChild(child, childName);
            if (found != null) return found;
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Editor utility (solo Play Mode)
    // -------------------------------------------------------------------------

#if UNITY_EDITOR
    [Header("DEBUG (Play Mode only)")]
    [SerializeField] private int debugLocalPlayerNumber = 1;

    [ContextMenu("Debug: Simula 1 utente")] private void DebugSim1() => SimulateCount(1);
    [ContextMenu("Debug: Simula 2 utenti")] private void DebugSim2() => SimulateCount(2);
    [ContextMenu("Debug: Simula 3 utenti")] private void DebugSim3() => SimulateCount(3);
    [ContextMenu("Debug: Simula 4 utenti")] private void DebugSim4() => SimulateCount(4);

    private void SimulateCount(int count)
    {
        lastActiveCount       = -1;
        lastLocalPlayerNumber = -1;
        localPlayerNumber     = Mathf.Clamp(debugLocalPlayerNumber, 1, 4);
        int clamped = Mathf.Clamp(count, 1, 4);
        Debug.Log($"[WorkstationManager] SIMULAZIONE: {clamped} utenti | LocalPlayer={localPlayerNumber}");

        for (int i = 0; i < workstations.Length; i++)
        {
            if (workstations[i] == null) continue;
            int  wn     = i + 1;
            bool active = wn <= clamped;
            workstations[i].SetActive(active);
            if (active) UpdateVirtualWhiteboards(workstations[i], wn, clamped);
        }
    }
#endif
}