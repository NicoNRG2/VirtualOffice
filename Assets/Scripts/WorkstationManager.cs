using System.Linq;
using System.Collections;
using UnityEngine;
using Ubiq.Rooms;

/// <summary>
/// Activates/deactivates Workstations and Virtual_Whiteboard objects based on
/// the number of users connected to the Ubiq room (1–4).
///
/// Slot assignment: first-come, first-served, based on arrival order in the room.
/// On join, the client counts how many peers already have a slot assigned and
/// claims the first free one. The slot is written to a peer property ONCE and
/// never changes for the entire session, even if other users enter or leave.
/// </summary>
public class WorkstationManager : MonoBehaviour
{
    [Header("Workstations (index 0 = Workstation1, index 3 = Workstation4)")]
    [SerializeField] private GameObject[] workstations = new GameObject[4];

    // Key used to store the slot number in Ubiq's per-peer property dictionary,
    // so all clients can read each other's assigned slot.
    private const string SLOT_KEY = "wm_slot";

    private RoomClient roomClient;

    private int  localPlayerNumber    = 0;   // 0 = not yet assigned
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

    // Called when this client successfully joins a room. Resets all state and
    // clears any stale slot property left from a previous session before claiming a new slot.
    private void OnJoinedRoom(IRoom room)
    {
        localPlayerNumber     = 0;
        lastActiveCount       = -1;
        lastLocalPlayerNumber = -1;

        if (roomClient?.Me != null)
            roomClient.Me[SLOT_KEY] = "";

        StartCoroutine(ClaimSlotCoroutine());
    }

    // Any peer addition or removal triggers a visibility refresh so the scene
    // always reflects the current number of active users.
    private void OnPeerChanged(IPeer peer) => RefreshVisibility();

    // -------------------------------------------------------------------------
    // Slot assignment — first-come, first-served, written once
    // -------------------------------------------------------------------------

    /// <summary>
    /// Waits for Ubiq's initial peer-property sync to complete, reads which slots
    /// are already taken, and claims the lowest free slot.
    /// Retries after 2 s if all four slots appear occupied (transient sync issue).
    /// </summary>
    private IEnumerator ClaimSlotCoroutine()
    {
        // Conservative delay to let peer properties from already-connected clients propagate.
        // Increase to ~1.5 s for high-latency remote servers.
        yield return new WaitForSeconds(0.8f);

        // Read the slot claimed by each peer already in the room.
        bool[] occupied = new bool[5]; // indices 1–4; index 0 is unused

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

        // Assign the first available slot and publish it so other peers can see it.
        for (int slot = 1; slot <= 4; slot++)
        {
            if (!occupied[slot])
            {
                localPlayerNumber    = slot;
                roomClient.Me[SLOT_KEY] = slot.ToString();
                Debug.Log($"[WorkstationManager] Slot assegnato: Player {localPlayerNumber}");
                RefreshVisibility();
                yield break;
            }
        }

        // All slots occupied (>4 users or sync still in progress) — retry after a short wait.
        Debug.LogWarning("[WorkstationManager] Nessuno slot libero, riprovo tra 2s...");
        yield return new WaitForSeconds(2f);
        StartCoroutine(ClaimSlotCoroutine());
    }

    // -------------------------------------------------------------------------
    // Core visibility logic
    // -------------------------------------------------------------------------

    // Enables workstations 1..N (where N = connected user count) and hides the rest.
    // On the local player's own workstation, also shows/hides the virtual whiteboards
    // that mirror the other players' boards.
    private void RefreshVisibility()
    {
        if (roomClient == null) return;

        // +1 because Peers does not include the local client.
        int activeCount = Mathf.Clamp(roomClient.Peers.Count() + 1, 1, 4);

        // Skip redundant updates; localPlayerNumber == 0 always forces a refresh.
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
    /// On the local player's workstation, enables the Virtual_Whiteboard_j objects
    /// for every other active player j, so they act as mirrors of remote boards.
    /// On workstations that do not belong to the local player, all virtual monitors are hidden.
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

            // First try a direct child lookup, then fall back to a deep recursive search.
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

    // Recursively searches a transform hierarchy for a child with a specific name.
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
    // Editor utility — only compiled in the Unity Editor, not in builds
    // -------------------------------------------------------------------------

#if UNITY_EDITOR
    [Header("DEBUG (Play Mode only)")]
    [SerializeField] private int debugLocalPlayerNumber = 1;

    [ContextMenu("Debug: Simula 1 utente")] private void DebugSim1() => SimulateCount(1);
    [ContextMenu("Debug: Simula 2 utenti")] private void DebugSim2() => SimulateCount(2);
    [ContextMenu("Debug: Simula 3 utenti")] private void DebugSim3() => SimulateCount(3);
    [ContextMenu("Debug: Simula 4 utenti")] private void DebugSim4() => SimulateCount(4);

    // Forces a specific user count in Play Mode without needing real network peers.
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