using System.Linq;
using UnityEngine;
using Ubiq.Rooms;

/// <summary>
/// Attiva/disattiva le Workstation e i Virtual_Whiteboard in base
/// al numero di utenti connessi alla stanza Ubiq (1-4).
///
/// Setup nel Inspector:
///   - Assegna i 4 GameObject Workstation1..4 all'array "workstations"
///     nell'ordine 0=Workstation1, 1=Workstation2, ecc.
///   - Assicurati che ogni Workstation contenga i GameObjects
///     Virtual_Whiteboard_1..4 (eccetto il proprio numero).
/// </summary>
public class WorkstationManager : MonoBehaviour
{
    [Header("Workstations (index 0 = Workstation1, index 3 = Workstation4)")]
    [SerializeField] private GameObject[] workstations = new GameObject[4];

    private RoomClient roomClient;
    private int lastActiveCount = -1; // -1 forza il refresh iniziale

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        // RoomClient.Find cerca nel NetworkScene corrente
        roomClient = RoomClient.Find(this);

        if (roomClient == null)
        {
            Debug.LogError("[WorkstationManager] RoomClient non trovato! " +
                           "Assicurati che questo GameObject sia figlio del NetworkScene.");
            return;
        }

        // Ubiq 1.x events
        roomClient.OnJoinedRoom.AddListener(OnJoinedRoom);
        roomClient.OnPeerAdded.AddListener(OnPeerAdded);
        roomClient.OnPeerRemoved.AddListener(OnPeerRemoved);

        // Stato iniziale (utente singolo prima di entrare in stanza)
        RefreshVisibility();
    }

    private void OnDestroy()
    {
        if (roomClient == null) return;
        roomClient.OnJoinedRoom.RemoveListener(OnJoinedRoom);
        roomClient.OnPeerAdded.RemoveListener(OnPeerAdded);
        roomClient.OnPeerRemoved.RemoveListener(OnPeerRemoved);
    }

    // -------------------------------------------------------------------------
    // Ubiq callbacks
    // -------------------------------------------------------------------------

    private void OnJoinedRoom(IRoom room) => RefreshVisibility();
    private void OnPeerAdded(IPeer peer)   => RefreshVisibility();
    private void OnPeerRemoved(IPeer peer) => RefreshVisibility();

    // -------------------------------------------------------------------------
    // Core logic
    // -------------------------------------------------------------------------

    private void RefreshVisibility()
    {
        if (roomClient == null) return;

        // roomClient.Peers contiene SOLO i peer remoti, non l'utente locale
        // quindi aggiungiamo 1 per includere noi stessi.
        int activeCount = Mathf.Clamp(roomClient.Peers.Count() + 1, 1, 4);

        // Evita aggiornamenti ridondanti
        if (activeCount == lastActiveCount) return;
        lastActiveCount = activeCount;

        Debug.Log($"[WorkstationManager] Utenti attivi: {activeCount}");

        for (int i = 0; i < workstations.Length; i++)
        {
            if (workstations[i] == null)
            {
                Debug.LogWarning($"[WorkstationManager] workstations[{i}] è null! " +
                                 "Assegna tutti e 4 i prefab nel Inspector.");
                continue;
            }

            int workstationNumber = i + 1; // 1-based (1..4)
            bool shouldBeActive   = workstationNumber <= activeCount;

            workstations[i].SetActive(shouldBeActive);

            if (shouldBeActive)
                UpdateVirtualWhiteboards(workstations[i], workstationNumber, activeCount);
        }
    }

    /// <summary>
    /// Per la workstation indicata, mostra/nasconde i Virtual_Whiteboard_j
    /// in base a quante postazioni sono attive.
    /// </summary>
    private void UpdateVirtualWhiteboards(GameObject workstation,
                                          int ownWorkstationNumber,
                                          int activeCount)
    {
        for (int j = 1; j <= 4; j++)
        {
            // La postazione non ha il proprio virtual whiteboard
            if (j == ownWorkstationNumber) continue;

            string vwName = $"Virtual_Whiteboard_{j}";

            // Cerca prima come figlio diretto, poi in profondità
            Transform vwTransform = workstation.transform.Find(vwName)
                                 ?? FindDeepChild(workstation.transform, vwName);

            if (vwTransform == null)
            {
                // Warning solo se ci aspettiamo che esista
                // (j è attivo ma non trovato → problema di naming)
                if (j <= activeCount && j != ownWorkstationNumber)
                    Debug.LogWarning($"[WorkstationManager] '{vwName}' non trovato " +
                                     $"in Workstation{ownWorkstationNumber}. " +
                                     "Controlla il nome nel prefab.");
                continue;
            }

            // Mostra il virtual whiteboard solo se quella postazione è attiva
            bool show = j <= activeCount;
            vwTransform.gameObject.SetActive(show);
        }
    }

    /// <summary>
    /// Ricerca ricorsiva di un Transform per nome.
    /// Utile se i Virtual_Whiteboard sono annidati sotto un canvas/pivot.
    /// </summary>
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
    // Editor utility: anteprima nell'Inspector (solo in Play Mode)
    // -------------------------------------------------------------------------

#if UNITY_EDITOR
    [ContextMenu("Debug: Simula 1 utente")]
    private void DebugSim1() => SimulateCount(1);

    [ContextMenu("Debug: Simula 2 utenti")]
    private void DebugSim2() => SimulateCount(2);

    [ContextMenu("Debug: Simula 3 utenti")]
    private void DebugSim3() => SimulateCount(3);

    [ContextMenu("Debug: Simula 4 utenti")]
    private void DebugSim4() => SimulateCount(4);

    private void SimulateCount(int count)
    {
        lastActiveCount = -1; // forza refresh
        int clamped = Mathf.Clamp(count, 1, 4);
        Debug.Log($"[WorkstationManager] SIMULAZIONE: {clamped} utente/i");

        for (int i = 0; i < workstations.Length; i++)
        {
            if (workstations[i] == null) continue;
            int wn = i + 1;
            bool active = wn <= clamped;
            workstations[i].SetActive(active);
            if (active)
                UpdateVirtualWhiteboards(workstations[i], wn, clamped);
        }
    }
#endif
}
