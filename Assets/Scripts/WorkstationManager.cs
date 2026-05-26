using System.Linq;
using UnityEngine;
using Ubiq.Rooms;

/// <summary>
/// Attiva/disattiva le Workstation e i Virtual_Whiteboard in base
/// al numero di utenti connessi alla stanza Ubiq (1-4).
///
/// Regola visibilità Virtual_Whiteboard:
///   - Il giocatore locale vede i Virtual_Whiteboard SOLO nella propria
///     workstation (quelli delle altre postazioni vengono sempre nascosti).
///   - Vengono mostrati solo i VW delle postazioni effettivamente attive.
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

    // Chiave Room Property: "uuid1,uuid2,uuid3,uuid4" (posizione = workstation - 1)
    private const string SLOT_KEY = "wm_slots";

    private RoomClient roomClient;
    private int lastActiveCount   = -1; // -1 forza il refresh iniziale
    private int localPlayerNumber =  0; // 0 = non ancora assegnato

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        roomClient = RoomClient.Find(this);

        if (roomClient == null)
        {
            Debug.LogError("[WorkstationManager] RoomClient non trovato! " +
                           "Assicurati che questo GameObject sia figlio del NetworkScene.");
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

    private void OnJoinedRoom(IRoom room) { ClaimSlot(); RefreshVisibility(); }
    private void OnPeerAdded(IPeer peer)  => RefreshVisibility();
    private void OnPeerRemoved(IPeer peer)=> RefreshVisibility();
    private void OnRoomUpdated(IRoom room){ ReadMySlot(); RefreshVisibility(); }

    // -------------------------------------------------------------------------
    // Slot assignment (Room Properties)
    // -------------------------------------------------------------------------

    private void ClaimSlot()
    {
        if (roomClient.Room == null) return;

        string myUuid = roomClient.Me.uuid;
        string[] slots = ParseSlots(roomClient.Room[SLOT_KEY]);

        // Già registrato?
        for (int i = 0; i < 4; i++)
        {
            if (slots[i] == myUuid) { localPlayerNumber = i + 1; return; }
        }

        // Primo slot libero
        for (int i = 0; i < 4; i++)
        {
            if (string.IsNullOrEmpty(slots[i]))
            {
                slots[i] = myUuid;
                localPlayerNumber = i + 1;
                roomClient.Room[SLOT_KEY] = string.Join(",", slots);
                Debug.Log($"[WorkstationManager] Slot assegnato: Player {localPlayerNumber}");
                return;
            }
        }

        Debug.LogWarning("[WorkstationManager] Nessuno slot libero!");
    }

    private void ReadMySlot()
    {
        if (roomClient?.Room == null) return;
        string myUuid = roomClient.Me.uuid;
        string[] slots = ParseSlots(roomClient.Room[SLOT_KEY]);

        for (int i = 0; i < 4; i++)
        {
            if (slots[i] == myUuid) { localPlayerNumber = i + 1; return; }
        }

        ClaimSlot(); // non ancora registrato
    }

    private static string[] ParseSlots(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return new string[4] { "", "", "", "" };
        var parts = raw.Split(',');
        var result = new string[4] { "", "", "", "" };
        for (int i = 0; i < Mathf.Min(parts.Length, 4); i++) result[i] = parts[i];
        return result;
    }

    // -------------------------------------------------------------------------
    // Core logic
    // -------------------------------------------------------------------------

    private void RefreshVisibility()
    {
        if (roomClient == null) return;

        int activeCount = Mathf.Clamp(roomClient.Peers.Count() + 1, 1, 4);

        if (activeCount == lastActiveCount && localPlayerNumber != 0) return;
        lastActiveCount = activeCount;

        Debug.Log($"[WorkstationManager] Utenti: {activeCount} | LocalPlayer: {localPlayerNumber}");

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
    /// Mostra i Virtual_Whiteboard solo se questa è la workstation del
    /// giocatore locale; nelle altre le nasconde tutte.
    /// Applica anche la regola base: non mostrare VW di postazioni inattive.
    /// </summary>
    private void UpdateVirtualWhiteboards(GameObject workstation,
                                          int ownWorkstationNumber,
                                          int activeCount)
    {
        // È la postazione del giocatore locale?
        bool isMyWorkstation = (localPlayerNumber != 0 &&
                                ownWorkstationNumber == localPlayerNumber);

        for (int j = 1; j <= 4; j++)
        {
            if (j == ownWorkstationNumber) continue; // non esiste per design

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

            // Visibile solo se: sono nella mia postazione E la postazione j è attiva
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
        lastActiveCount   = -1;
        localPlayerNumber = Mathf.Clamp(debugLocalPlayerNumber, 1, 4);
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