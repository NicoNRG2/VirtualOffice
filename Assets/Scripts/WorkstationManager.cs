using System.Linq;
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

    // Chiave Room Property: "uuid1,uuid2,uuid3,uuid4" (posizione = workstation - 1)
    private const string SLOT_KEY = "wm_slots";

    private RoomClient roomClient;
    private int lastActiveCount   = -1;
    private int localPlayerNumber =  0; // 0 = non ancora assegnato

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
        ClaimSlot();
        RefreshVisibility();
    }

    private void OnPeerAdded(IPeer peer) => RefreshVisibility();

    private void OnPeerRemoved(IPeer peer)
    {
        // FIX: rimuovi lo slot del peer disconnesso, così è riutilizzabile
        ReleaseSlot(peer.uuid);
        RefreshVisibility();
    }

    private void OnRoomUpdated(IRoom room)
    {
        ReadMySlot();
        RefreshVisibility();
    }

    // -------------------------------------------------------------------------
    // Slot assignment (Room Properties)
    // -------------------------------------------------------------------------

    private void ClaimSlot()
    {
        if (roomClient?.Room == null) return;

        string myUuid = roomClient.Me.uuid;
        string[] slots = ParseSlots(roomClient.Room[SLOT_KEY]);

        // Già registrato?
        for (int i = 0; i < 4; i++)
        {
            if (slots[i] == myUuid) { localPlayerNumber = i + 1; return; }
        }

        // FIX: pulizia slot zombie (UUID non più presenti come peer né come Me)
        var activePeerUuids = roomClient.Peers.Select(p => p.uuid).ToHashSet();
        activePeerUuids.Add(myUuid);

        for (int i = 0; i < 4; i++)
        {
            if (!string.IsNullOrEmpty(slots[i]) && !activePeerUuids.Contains(slots[i]))
            {
                Debug.Log($"[WorkstationManager] Slot {i + 1} zombie rimosso (UUID: {slots[i]})");
                slots[i] = "";
            }
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
                ForceRefreshAfterSlotAssign();
                return;
            }
        }

        Debug.LogWarning("[WorkstationManager] Nessuno slot libero!");
    }

    /// <summary>
    /// Rimuove l'UUID specificato dagli slot e scrive la Room Property aggiornata.
    /// Chiamato quando un peer si disconnette.
    /// </summary>
    private void ReleaseSlot(string uuid)
    {
        if (roomClient?.Room == null || string.IsNullOrEmpty(uuid)) return;

        string[] slots = ParseSlots(roomClient.Room[SLOT_KEY]);
        bool changed = false;

        for (int i = 0; i < 4; i++)
        {
            if (slots[i] == uuid)
            {
                slots[i] = "";
                changed = true;
                Debug.Log($"[WorkstationManager] Slot {i + 1} rilasciato (UUID: {uuid})");
            }
        }

        if (changed)
            roomClient.Room[SLOT_KEY] = string.Join(",", slots);
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

        ClaimSlot();
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

        // FIX: non fare early-return se localPlayerNumber è ancora 0,
        // perché dobbiamo aggiornare i VW appena lo slot viene assegnato.
        // Confronta sia activeCount che localPlayerNumber per evitare refresh inutili.
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
    // FIX: forza refresh dei VW dopo che lo slot viene assegnato
    // -------------------------------------------------------------------------

    /// <summary>
    /// Chiamato da ClaimSlot dopo aver assegnato localPlayerNumber > 0.
    /// Forza un RefreshVisibility completo anche se activeCount non è cambiato.
    /// </summary>
    private void ForceRefreshAfterSlotAssign()
    {
        lastActiveCount = -1; // invalida la cache per forzare il refresh completo
        RefreshVisibility();
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