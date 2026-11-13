# Coop Physics Prototype (Unity 6 / Netcode for GameObjects)

Small co-op prototype for 2–4 players: players connect to a host (listen server), meet in a lobby,
load into a level together and interact with shared physics items.

- Unity **6000.0.85f2 (6.0 LTS)**, Built-in render pipeline
- **Netcode for GameObjects 2.14**, **Unity Transport 2.7** (direct IP) and **Steam Networking Sockets** (Steam P2P)
- **VContainer** (DI), **UniTask**, **New Input System**, uGUI
- Steamworks.NET 2025.163.0, Steam transport: [multiplayer-community-contributions / SteamNetworkingSockets](https://github.com/Unity-Technologies/multiplayer-community-contributions/tree/main/Transports/com.community.netcode.transport.steamnetworkingsockets)
- Dev tools: Multiplayer Play Mode, Multiplayer Tools (Network Simulator)

---

## 1. Running

### Download the build (Windows)
Ready-to-run build: **[Releases → latest](https://github.com/raelUnitydev/CoopPrototype_TestTask/releases/latest)**.
Download the zip, extract it anywhere and run `CoopPrototype.exe` (`steam_appid.txt` is already next to the exe).
Quick local test without Steam: start two copies on one PC and use **Direct IP** (see below) with `127.0.0.1:7777`.
Windows Firewall may ask for network access on the first **Host**. Allow it, otherwise other PCs cannot connect.

### Build from source
Open the project in Unity 6000.0.85f2. The build goes to `Builds/Windows/CoopPrototype.exe` (build from the editor: **Tools > Coop > Build Windows**;
**Tools > Coop > Generate Project Content** regenerates scenes/prefabs/configs from code).
`steam_appid.txt` (480) is written next to the exe by the build step.

### Direct IP (Unity Transport)
1. Start the exe, press **Host** (listens on `0.0.0.0:7777`). The lobby shows the LAN address to share.
2. On other instances/PCs enter `ip:port` and press **Join**.
3. Host presses **Start** in the lobby.

Several instances on one PC work out of the box: each running instance claims its own local profile
(named mutex), so it gets a distinct PlayerId and is not rejected as a duplicate.

In the Editor use **Multiplayer Play Mode** (Window > Multiplayer > Multiplayer Play Mode) — the editor
defaults to direct IP (`AppConfig.EditorPlatform`).

### Steam (P2P, AppID 480)
1. Two PCs, two different Steam accounts, Steam client running and logged in on both.
2. Start the exe (the default platform in builds is `Auto` = Steam if it initializes).
3. Host: set lobby name / max players / visibility (Friends or Private), press **Create Steam lobby**.
4. Others join by: **Invite** from the lobby (Steam overlay, Shift+Tab), **Join Game** in the friends list,
   or by entering the lobby ID (shown in the lobby) and pressing **Join**.

Steamworks.NET initialization needs `steam_appid.txt` with `480` next to the exe (and in the project root
for the Editor). If Steam is not running, the game does not crash: it shows a message and falls back to direct IP.

### Command line
| Argument | Meaning |
|---|---|
| `-transport utp\|steam` | Choose platform/transport (overrides `AppConfig`) |
| `-host` | Host immediately |
| `-join <ip:port \| lobbyId>` | Join immediately |
| `-name <name>` | Player name (direct IP mode) |
| `-port <port>` | Port (default 7777) |
| `-profile <n>` | Force local profile slot (PlayerId) |
| `-maxplayers <n>` | Max players (1–4) |
| `-buildversion <v>` | Override build version — to demonstrate the "different version" rejection |
| `-netsim <delayMs> [loss%]` | Simulate latency / packet loss (see below) |

Example: `CoopPrototype.exe -transport utp -host -name Alice` and `CoopPrototype.exe -transport utp -join 127.0.0.1:7777 -name Bob`.

### Controls
| Key | Action |
|---|---|
| WASD / mouse, Space, Shift | Move, look, jump, sprint |
| E (or LMB) | Grab item under the crosshair |
| LMB while holding | Throw |
| Q or E while holding | Drop |
| X while holding | Delete item |
| F / R | Spawn item / cycle item type |
| C | Change color |
| Tab | Player list (names, ping, Kick for host) |
| Esc | Pause menu (Resume / Players / Leave) |

### Testing under latency / loss
`-netsim <delayMs> [lossPercent]` adds the Multiplayer Tools **Network Simulator** to the NetworkManager
(Unity Transport, development build / Editor). The delay is applied to this peer's traffic in each direction,
so `-netsim 75 5` on a client gives ~150 ms RTT with 5 % loss, `-netsim 150 5` gives ~300 ms RTT.

```
CoopPrototype.exe -transport utp -host -name Host
CoopPrototype.exe -transport utp -join 127.0.0.1:7777 -name Lag -netsim 75 5
```
The player list (Tab) shows the measured RTT of every player.

---

## 2. Architecture

Clean-architecture rings with feature assemblies (asmdef = compile-time enforced boundaries):

```
                 Game.App  (composition root: VContainer RootLifetimeScope, AppConfig, CLI)
                    │  the ONLY assembly that knows every module and picks the platform
     ┌──────────────┼───────────────────────┬─────────────────────┬──────────────┐
Platform.Local   Platform.Steam          Features.Items  ──►  Features.Player   Game.UI
(UTP, GUID id,   (Steamworks, Steam      (NetworkItem,        (character,       (uGUI MVP)
 stats stub)      transport, lobby,       ItemAuthority)        input)
                  presence, stats)              │                   │              │
     └──────────────┴──────────► Game.Net ◄─────┴───────────────────┘              │
                         (NGO session service, approval, roster,                   │
                          scene flow, prefab injection)                            │
                                    │                                              │
                          Game.Application  ◄──────────────────────────────────────┘
                          (ports/interfaces, AppFlow, AchievementTracker)
                                    │
                          Game.Domain  (pure C#, noEngineReferences:
                          ApprovalPolicy, ItemRules, PlayerId, ReactiveProperty)
```

- **Domain** has no Unity/Netcode references — host-side rules (approval, grab/throw validation) are
  pure functions covered by EditMode tests (`Assets/_Project/Scripts/Tests`).
- **Application** defines ports: `ISessionService`, `IPlayerRoster`, `IMatchmakingService`,
  `IPlayerIdentity`, `IRichPresence`, `IStatsService`, ... `AppFlow` coordinates sessions and maps every
  end of a session to "back to menu + reason".
- **Net** implements the session on NGO; it never references Steam or a concrete transport
  (only `INetworkTransportProvider` / `IMatchmakingService`).
- **Features** (gameplay) reference Net/Application, never Steam or a transport — the compiler enforces it.
- **Steamworks is referenced only by `Platform.Steam`** (and `App`, which installs it).
- Network-spawned objects get dependencies through a Netcode prefab handler that instantiates via the
  container (`NetworkPrefabInjector`) — no `FindObjectOfType` / service locator in prefabs.
- Two independent state axes (as in Boss Room): connection status (`Offline → Connecting/StartingHost →
  Connected/Hosting → ShuttingDown`) and the replicated session phase (`Lobby → Loading → InGame`).

### Extension points
- **New transport/platform:** new `Platform.X` assembly implementing `INetworkTransportProvider`,
  `IMatchmakingService` (+ identity/presence/stats), an `IPlatformInstaller` in `App/PlatformInstallers.cs`
  and a case in `PlatformSelector`. No gameplay or session code changes.
- **New item type:** prefab with `NetworkObject` (DontDestroyWithOwner), `Rigidbody`, `NetworkTransform`
  (Owner mode), `NetworkItem`; add it to `Config/ItemCatalog`. It is registered as a network prefab automatically.

---

## 3. Networking decisions

### Authority
| Entity | Authority | Why |
|---|---|---|
| Session (roster, phase, colors, ping) | Host (`SessionNetworkState`, server-written `NetworkList`/`NetworkVariable`) | Single source of truth, late joiners get it on spawn |
| Character movement | **Owner** (owner-mode `NetworkTransform`) | See below |
| Free item | Host (owner = server, host-only physics) | One simulation, no conflicts |
| Held item | Holder (ownership transferred on grab) | Holder moves it without latency |
| Spawn / grab / release / throw / delete | Host validates every request | Clients only *request* |

### Character: owner-authoritative
The owner simulates its own `CharacterController`; position + yaw are replicated by an owner-mode
`NetworkTransform` (half precision, unreliable deltas, thresholds), remote players are interpolated.
Reasons: under 150 ms RTT movement stays instant for the local player, NGO has no built-in prediction/
reconciliation (writing it by hand is a lot of code and a big desync risk), and in a co-op game cheating
on movement is not a threat model. Everything that changes the *world* is still validated by the host.
The camera is created locally for the owner and is not part of the networked prefab.

### Items: grab / throw
- **Grab:** owner sends `GrabRpc` → host checks `ItemRules.CanGrab` (item free, requester not holding
  something, distance within reach + latency tolerance, measured with the host's view of the player).
  On success it sets `Holder` (server `NetworkVariable`) and `ChangeOwnership(holder)`.
  The client never grabs optimistically, so when two players grab simultaneously the host processes
  the requests in order: the first wins, the second gets a rejection RPC ("someone else grabbed it") — no desync.
- **Held:** the holder moves the item kinematically to a point in front of the camera; colliders are
  disabled while held. Others see it through the (now holder-authoritative) `NetworkTransform`.
- **Release / throw:** the holder stops driving the item immediately and sends `ReleaseRpc(position,
  rotation, velocity)`. The host validates holder + release offset, clamps speed, takes ownership back
  (`RemoveOwnership`), places the item at the reported position and applies the velocity.
  From then on **only the host simulates** the flight; all other peers (including the thrower) keep the
  rigidbody kinematic and interpolate. Because nobody runs a competing local simulation, there are no
  corrections — the item flies the same for everyone and does not teleport when landing.
  The cost: the thrower sees the throw start ~1 RTT later (item waits at the release point).
- **Delete:** only the holder may delete (`CanDelete`), host despawns.
- **Holder disconnects:** items have `DontDestroyWithOwner`; the host releases the leaver's item
  (ownership back to host, physics on) and it falls.
- **Spawn:** host spawns the item owned by the server (not by the creator), rate-limited per player and capped.

### Session lifecycle
- **Approval** (`ConnectionApprovalHandler` → `ApprovalPolicy`): payload = build version, PlayerId,
  name. Rejects with a reason: different version, duplicate PlayerId, session full, loading in progress.
  The reason travels in NGO's disconnect reason string (`ADG:<code>`) and is shown on the client.
  Timeout and kick are also mapped to readable messages.
  The approval registers the client synchronously, so two simultaneous connections cannot take the same slot/id.
- **Scene loading:** host loads via `NetworkSceneManager`, keeps the set of clients it waits for, removes
  clients on `LoadComplete` **and on disconnect**, and flips the replicated phase to `InGame` when the set is
  empty — the phase change reaches everybody in the same tick, so loading screens close simultaneously and
  a client dropping mid-load never blocks the others. The host's roster also shows each client's load state.
- **Late join:** joining is allowed in `Lobby` and `InGame` (rejected only during `Loading`). NGO scene
  synchronization loads the current level on the joiner only; others are not affected. The joiner receives
  roster, colors and all items (`Holder` is a NetworkVariable, so "who holds what" is part of the spawn state),
  and gets its character after `SynchronizeComplete`.
- **Client leaves** (button, Alt+F4, network loss): NGO despawns its character; the host updates the
  roster and releases its item.
- **Host leaves:** a graceful shutdown first sends `HostClosingRpc` so clients show "host closed the session";
  a crash is detected by transport timeout and shown as "connection lost". Either way clients return to the menu.

### What goes over the network
| Data | Channel | Rate |
|---|---|---|
| Character transform | NetworkTransform, half floats, unreliable deltas, pos + yaw only | tick (30 Hz) **only when changed** |
| Item transform | same, full rotation | only while moving (sleeping items send nothing) |
| Requests (grab/throw/spawn/delete/color) | reliable RPC, owner-only | on input |
| Roster row (name, color, ping, loaded) | `NetworkList` delta | on change; ping at most 1/s and only if it moved ≥ 3 ms |
| Ping | unreliable RPC pair, application level | 1 Hz per client |
| Phase | `NetworkVariable<byte>` | on change |
Tick rate is 30 Hz (instead of 60) — enough for co-op and half the traffic.
Ping is measured at the application level so it works the same on every transport
(the Steam transport does not report RTT).

### Steam
- Lobby (friends-only/private, name, member limit) is used for discovery/invites; the game connection
  is NGO over Steam Networking Sockets P2P to the lobby owner's SteamID (relay, no IP / port forwarding).
- Join paths: overlay invite (`GameLobbyJoinRequested_t`), "Join Game" (`connect` rich presence →
  `GameRichPresenceJoinRequested_t` / `+connect_lobby` launch arg), lobby ID.
- Lobby mirrors the session: the host updates joinable (`players < max && not loading`), member limit,
  player count and phase. When the session ends everyone leaves the lobby; members also leave as soon as
  the lobby owner leaves, so the lobby never outlives the host.
- Identity: PlayerId = SteamID64, name = persona name.
- Rich Presence: `status` = "In lobby (2/4)" / "In game 2/4", `steam_player_group(_size)`, `connect`.
- Stats/achievements: `IStatsService`. The **host** validates the gameplay event (e.g. a throw) and sends it
  to the acting player; the **client that owns the stats** increments it (Steam stats are per local user)
  and calls `StoreStats` in batches: immediately when an achievement unlocks, otherwise when the session ends.
  Steam implementation uses fictional API names (calls are logged), the direct-IP build uses a logging stub.

---

## 4. Known issues / what I would do next
- **Throw latency for the thrower:** the item waits ~1 RTT at the release point. Fix: client-side
  ballistic prediction of the first frames and blending into the host's state.
- **Movement is trusted** (owner-authoritative). For a competitive game: server-authoritative movement with
  input prediction + reconciliation, or at least host-side speed/teleport sanity checks.
- **Held item vs. holder interpolation:** remote peers interpolate the character and the held item
  separately, a small relative wobble is possible at high latency. Fix: parent the item to the character
  (NetworkObject parenting) while held.
- **Steam identity is self-reported** in the approval payload. Fix: Steam auth session tickets
  (`GetAuthSessionTicket` / `BeginAuthSession`) validated by the host.
- **Steam transport** reports RTT 0 (we use an app-level ping) and allocates per message; a production build
  would use a maintained transport.
- **Rich presence** uses the raw `status` key — localized `steam_display` tokens need a Steamworks backend
  config that AppID 480 does not allow.
- Joining during a level load is rejected ("try again") instead of queued.
- No host migration (not required).

---

## Time spent
≈ 11 hours.
