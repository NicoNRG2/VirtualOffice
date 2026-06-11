using System.Linq;
using System.Collections;
using UnityEngine;
using Ubiq.Rooms;

/// <summary>
/// Attiva/disattiva le Workstation e i Virtual_Whiteboard in base
/// al numero di utenti connessi alla stanza Ubiq (1-4).
///
/// Assegnazione slot: basata sull'ordine di arrivo nella stanza.
/// Al join, il client conta quanti peer hanno già uno slot assegnato
/// e prende il primo libero. Lo slot viene scritto nella peer property
/// UNA SOLA VOLTA e non cambia mai per tutta la sessione, anche se
/// entrano o escono altri utenti.
/// </summary>
public class WorkstationManager : MonoBehaviour
{
    [Header("Workstations (index 0 = Workstation1, index 3 = Workstation4)")]
    [SerializeField] private GameObject[] workstations = new GameObject[4];

    private const string SLOT_KEY = "wm_slot";

    private RoomClient roomClient;

    private int  localPlayerNumber    = 0;   // 0 = non ancora assegnato
    private int  lastActiveCount      = -1;
    private int  lastLocalPlayerNumber = -1;

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
        roomClient.OnPeerAdded.AddListener(OnPeerChanged);
        roomClient.OnPeerRemoved.AddListener(OnPeerChanged);
    }

    private void OnDestroy()
    {
        if (roomClient == null) return;
        roomClient.OnJoinedRoom.RemoveListener(OnJoinedRoom);
        roomClient.OnPeerAdded.RemoveListener(OnPeerChanged);
        roomClient.OnPeerRemoved.RemoveListener(OnPeerChanged);
    }

    // -------------------------------------------------------------------------
    // Ubiq callbacks
    // -------------------------------------------------------------------------

    private void OnJoinedRoom(IRoom room)
    {
        // Reset completo ad ogni join (nuova stanza = nuovo slot)
        localPlayerNumber     = 0;
        lastActiveCount       = -1;
        lastLocalPlayerNumber = -1;

        // Cancella l'eventuale slot residuo dalla sessione precedente
        // così gli altri peer non lo leggono come "occupato"
        if (roomClient?.Me != null)
            roomClient.Me[SLOT_KEY] = "";

        StartCoroutine(ClaimSlotCoroutine());
    }

    private void OnPeerChanged(IPeer peer) => RefreshVisibility();

    // -------------------------------------------------------------------------
    // Slot assignment: ordine di arrivo, scritto una volta sola
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attende che le peer property dei peer già connessi arrivino,
    /// poi prende il primo slot libero e lo fissa per l'intera sessione.
    /// Il delay copre la finestra di sincronizzazione iniziale di Ubiq.
    /// </summary>
    private IEnumerator ClaimSlotCoroutine()
    {
        // Lascia propagare le peer property dei peer già presenti.
        // 0.8s è conservativo ma sicuro per LAN/localhost; aumenta
        // a 1.5s se usi server remoti con latenza alta.
        yield return new WaitForSeconds(0.8f);

        // Leggi gli slot già occupati dagli altri peer
        bool[] occupied = new bool[5]; // indici 1-4, 0 ignorato

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

        // Prendi il primo slot libero
        for (int slot = 1; slot <= 4; slot++)
        {
            if (!occupied[slot])
            {
                localPlayerNumber    = slot;
                roomClient.Me[SLOT_KEY] = slot.ToString(); // scrivi UNA SOLA VOLTA
                Debug.Log($"[WorkstationManager] Slot assegnato: Player {localPlayerNumber}");
                RefreshVisibility();
                yield break;
            }
        }

        // Tutti gli slot occupati (>4 utenti o sync ancora in corso): riprova
        Debug.LogWarning("[WorkstationManager] Nessuno slot libero, riprovo tra 2s...");
        yield return new WaitForSeconds(2f);
        StartCoroutine(ClaimSlotCoroutine());
    }

    // -------------------------------------------------------------------------
    // Core logic
    // -------------------------------------------------------------------------

    private void RefreshVisibility()
    {
        if (roomClient == null) return;

        int activeCount = Mathf.Clamp(roomClient.Peers.Count() + 1, 1, 4);

        // Salta se nulla è cambiato (localPlayerNumber == 0 forza sempre il refresh)
        if (localPlayerNumber != 0 &&
            activeCount       == lastActiveCount &&
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
    /// Mostra i Virtual_Whiteboard_j solo sulla workstation del giocatore locale.
    /// Sulle altre workstation i virtual monitor vengono nascosti.
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