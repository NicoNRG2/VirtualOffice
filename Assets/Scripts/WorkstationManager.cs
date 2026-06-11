using System.Linq;
using UnityEngine;
using Ubiq.Rooms;

/// <summary>
/// Attiva/disattiva le Workstation e i Virtual_Whiteboard in base
/// al numero di utenti connessi alla stanza Ubiq (1-4).
///
/// Assegnazione slot: deterministica, senza race condition.
/// Ogni client calcola il proprio slot ordinando tutti i peer uuid
/// (incluso il proprio) in modo lessicografico. Il posto nell'ordine
/// corrisponde al numero di slot (1 = uuid più piccolo).
/// Non viene scritta nessuna peer property: niente persistenza sporca,
/// niente write concorrenti.
/// </summary>
public class WorkstationManager : MonoBehaviour
{
    [Header("Workstations (index 0 = Workstation1, index 3 = Workstation4)")]
    [SerializeField] private GameObject[] workstations = new GameObject[4];

    private RoomClient roomClient;

    // Cache per evitare refresh ridondanti
    private int lastActiveCount       = -1;
    private int lastLocalPlayerNumber = -1;

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
        // Resetta la cache così il prossimo refresh non viene saltato
        lastActiveCount       = -1;
        lastLocalPlayerNumber = -1;
        RefreshVisibility();
    }

    private void OnPeerChanged(IPeer peer) => RefreshVisibility();

    // -------------------------------------------------------------------------
    // Slot assignment: deterministico, senza scritture
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calcola il numero di slot del client locale ordinando gli uuid
    /// di tutti i partecipanti (Me incluso) in modo lessicografico.
    /// Stessa logica su tutti i client → stesso risultato senza comunicazione.
    /// </summary>
    private int ComputeLocalSlot()
    {
        if (roomClient?.Me == null) return 0;

        // Raccoglie uuid di tutti i peer + il nostro
        var allUuids = roomClient.Peers
            .Select(p => p.uuid)
            .Append(roomClient.Me.uuid)
            .OrderBy(id => id)          // ordine lessicografico stabile
            .ToList();

        int index = allUuids.IndexOf(roomClient.Me.uuid);
        return index >= 0 ? index + 1 : 0; // slot 1-based, 0 = non trovato
    }

    // -------------------------------------------------------------------------
    // Core logic
    // -------------------------------------------------------------------------

    private void RefreshVisibility()
    {
        if (roomClient == null) return;

        // +1 perché Peers non include il client locale
        int activeCount      = Mathf.Clamp(roomClient.Peers.Count() + 1, 1, 4);
        int localPlayerNumber = ComputeLocalSlot();

        // Salta se nulla è cambiato
        if (activeCount       == lastActiveCount &&
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
                UpdateVirtualWhiteboards(workstations[i], workstationNumber, activeCount, localPlayerNumber);
        }
    }

    /// <summary>
    /// Mostra i Virtual_Whiteboard_j solo sulla workstation del giocatore locale.
    /// Sulle altre workstation i virtual monitor vengono nascosti.
    /// </summary>
    private void UpdateVirtualWhiteboards(GameObject workstation,
                                          int ownWorkstationNumber,
                                          int activeCount,
                                          int localPlayerNumber)
    {
        bool isMyWorkstation = (localPlayerNumber != 0 &&
                                ownWorkstationNumber == localPlayerNumber);

        for (int j = 1; j <= 4; j++)
        {
            if (j == ownWorkstationNumber) continue; // il monitor fisico non è un VW

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

            // Mostra il virtual monitor solo se:
            // - siamo sulla nostra workstation
            // - il peer a cui corrisponde è connesso
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

        int localPlayer = Mathf.Clamp(debugLocalPlayerNumber, 1, 4);
        int clamped     = Mathf.Clamp(count, 1, 4);

        Debug.Log($"[WorkstationManager] SIMULAZIONE: {clamped} utenti | LocalPlayer={localPlayer}");

        for (int i = 0; i < workstations.Length; i++)
        {
            if (workstations[i] == null) continue;
            int  wn     = i + 1;
            bool active = wn <= clamped;
            workstations[i].SetActive(active);
            if (active) UpdateVirtualWhiteboards(workstations[i], wn, clamped, localPlayer);
        }
    }
#endif
}