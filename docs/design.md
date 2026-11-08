# Violet Vaults — Game Design Document v0.2

> Status: living document. Sections marked **[OPEN]** are undecided; **[TUNE]** means the number is a first guess to be balanced in playtests.

## 1. Pitch

A **1–4 player semi-cooperative, real-time, top-down dungeon extraction roguelike**, online and local couch co-op.

Players descend together into a shifting vault. The dungeon's **Threat** is shared — every noisy, greedy action endangers the whole group — but **Points are personal**. Whoever extracts with the most Points wins the run. Help each other survive, race each other to the Relics, and try not to be the loudest one in the room when the vault strikes back.

Inspired by *Clank!* (shared danger, individual greed, noise attribution), with the moment-to-moment feel of an extraction game.

## 2. Pillars

1. **Greed vs. noise.** Every valuable action makes noise. The question is always "one more room?"
2. **Together, but not a team.** Cooperation is often necessary; it is never guaranteed.
3. **Readable tension.** The Threat meter, who is loud, and what's about to happen are always visible.
4. **Small decisions, often.** Which door, which card, grab the bag or run.

## 3. Visual & Audio Direction

- High-contrast 2D pixel art, top-down, one room fills the screen.
- Strict palette: deep purples, void black, stark white. White is reserved for value and danger (loot, Relics, Threat spikes, enemy tells).
- Sound is a mechanic: noisy actions should *sound* loud; Threat tiers change the ambient layer.

## 4. Run Structure

```
Hub (lobby + meta shop) → Descent → Explore / loot / play cards → Grab Relic(s)
  → Return to an exit → Extract (keep Points)  or  Die (drop your loot)
  → Run ends when every player has extracted or died → Scoreboard → Hub
```

### 4.1 Dungeon layout
- **Floors** (Depth 1–3 for the vertical slice **[TUNE]**). Each floor is a grid of rooms, ~6×6 **[TUNE]**.
- Every room has up to **4 exits** (N/E/S/W). Not every exit is open: exits can be **open, locked (standard key), sealed (Void Key), collapsed, one-way, or hidden**. Closed exits are what make the grid a maze and give keys a purpose.
- **Stairs** connect floors. Deeper = better loot, more Threat sensitivity.
- **Extraction** happens at the **Lobby** (Depth 1 entrance). Rare **Rope Lifts** deeper down allow early extraction at a cost (key or Points) **[OPEN]**.
- Rooms are hidden until entered or revealed (fog of war on the map).

### 4.2 Relics
- Each floor holds one or more **Relics** in a Relic Chamber; deeper Relics are worth more.
- Picking up a Relic: large Point value, **big immediate Threat spike**, and it can only be banked by extracting.
- A player can carry **one Relic** at a time **[TUNE]**.
- When the **first player extracts**, the **Collapse** begins: a countdown after which anyone still inside dies **[TUNE: 90s]**. This forces the endgame and creates the classic "do I go back for them?" moment.

### 4.2b Implemented (M5)
- The Relic Chamber holds **2 Relics (50p and 30p)** (`relicValues` in `dungeon.json`). Lifting one: its points join your carried points, **+12 Threat** (`takeRelic`), and a crown shows over your head and in the HUD. One at a time. If you die, its points go into your loot bag.
- **Escape** by standing in the Lobby's rune circle and pressing Interact: everything carried is **banked** and you're out of the dungeon (you spectate).
- The **Collapse** starts on the first escape *or* when Threat maxes out: **90s** (`collapseSeconds` in `threat.json`), a countdown at the top, the escape circle burns white. Anyone still inside at zero is **sealed in** and banks nothing.
- The run ends when nobody is left inside. **Scoreboard**: escaped / fell / sealed in, banked points, relic, and the **winner** (most banked among escapees; ties share). The host presses Enter for a **rematch** (new dungeon, same players).

### 4.3 Winning
- Score = Points banked by extracting. Dead players score 0 for the run.
- Highest score wins the run (bragging rights + a small meta bonus **[OPEN]**).
- Everyone who extracts still earns meta currency, so losing a run is never wasted.

## 5. The Player

### 5.1 Controls (twin-stick-ish, gamepad first, keyboard+mouse supported)
| Action | Notes |
|---|---|
| Move | Walking is quiet. |
| Sprint | Fast, **generates noise**. |
| Sneak | Slow, generates no movement noise. |
| Interact | Open chest / unlock door (keys are picked up by touch), later revive. Keyboard WASD: `E`, Arrows: `/`, gamepad: `A`. |
| Basic Attack | Short melee swing / shove that briefly stuns. Noisy. |
| Use Card 1–3 | Play a card from hand. |
| Ping | Mark a spot/item for teammates (essential online). |

### 5.2 Health & death (simple combat, may deepen later)
- **3 hearts** **[TUNE]**. Enemies hit for 1. Combat is about **avoidance and positioning**, not DPS races. The basic attack stuns and pushes back; most enemies can't be killed by it alone (cards and environment do that).
- At 0 hearts you are **Downed** for 20s **[TUNE]**: crawl slowly, can't act. An ally can **revive** you (hold Interact, noisy) — you come back at 1 heart.
- If not revived: you **die**, drop your **Loot Bag** (all carried Points, keys, Relic) in the room. **Anyone can pick it up.** You then spectate **[OPEN: ghost mechanics later]**.

### 5.2b Implemented (M4)
- Numbers in `game/data/enemies.json` (`health`): 3 hearts, 1.2s invulnerability after a hit, Dash grants 0.35s, 20s bleed-out, revive to 1 heart.
- **Downed** players crawl (22 px/s), can't sprint, interact or play cards. Enemies ignore them.
- **Reviving** by hand: stand beside them and press Interact; it takes **1.5s** and fails if you step away or go down (progress ring shown to everyone). Makes noise (+2). The **Rally** card revives instantly from anywhere in the room.
- **Death** drops a loot bag (all carried points and keys) where you last got hit; anyone can pick it up with Interact. Dead players spectate a teammate.
- If nobody is left standing, everyone downed dies and the run ends.
- No player basic attack yet — combat is avoidance plus cards (Flash Powder, Shatter, Decoy, Dash).

### 5.3 Carried vs. banked
- Everything picked up during a run is **carried** (at risk).
- Points are **banked** only by extracting.
- Meta currency on run end: extracted → 100% of banked Points; died → 25% of what you were carrying **[TUNE]**.

## 6. Threat (shared)

### 6.1 Generation
Threat is one shared meter (0–100 **[TUNE]**), split into **tiers**. Every point of Threat is **attributed** to the player who made it.

| Source | Threat |
|---|---|
| Passive drift | +1 every 10s **[TUNE]** — a clock; stalling is never free |
| Sprinting | small, continuous |
| Basic attack / combat | small per hit |
| Forcing a lock / breaking things | medium |
| Loud cards | per card (shown on card) |
| Picking up a Relic | large |
| Reviving | small |
| Echo Chamber room | all noise ×2 |
| Hush Room | no noise generated |

Stealth cards and rooms can **reduce** Threat or **shift attribution** to another player.

### 6.2 Attribution — the semi-co-op heart
Each player has a **Noise share** (their % of recent Threat). It's always visible on the HUD.
When the dungeon **Strikes** (see tiers), its target is **chosen at random, weighted by Noise share** — the Clank "bag" in real time. Loud players get hunted; quiet players get away with it… usually.

### 6.3 Tiers
| Tier | Effect |
|---|---|
| 0 Calm | Normal patrols. |
| 1 Stirring | Patrols speed up; occasional Strike (an enemy spawns near the chosen player). |
| 2 Restless | **The vault shifts**: some exits rotate/lock, Shifting Rooms change. More frequent Strikes. |
| 3 Hunting | Elite enemies spawn. Collapsed Passages open (shortcuts appear, others close). |
| 4 Furious | **The Warden** awakens: an unkillable hunter that tracks the highest-Noise player. |
| 5 Collapse | Max Threat triggers the Collapse countdown (same as first extraction). |

Threat never drops below the current tier's floor except by rare effects — tiers are ratchets.

### 6.3b Implemented (M4)
- **Enemies:** Hollow Sentries guard Guard Posts (1–2), Vaults (0–1) and the Relic Chamber (1) from the start. They patrol, notice walking players at 85px in their room, sprinting players at 140px (noise carries a little through doorways), Hushed players only at 30px; chase through open doorways (not locked ones); wind up a visible chop (eyes flare, "!") and hit within 20px; give up after 3s without noticing you.
- **Strikes:** from Stirring on, every 40 / 28 / 20 / 15 / 12 s (by tier) a Sentry comes in through a doorway of one player's room, picked at random **weighted by noise**. Cap of 16 enemies.
- **The Warden** awakens once at Furious, in the reachable room farthest from the loudest player, and hunts whoever is loudest: slower than walking (58 px/s), unkillable, barely stunned, never fooled by Decoys.
- Patrol speeds up / vault shifting / elites at higher tiers are not in yet.

### 6.4 Implemented (M2)
- Numbers live in `game/data/threat.json`: passive +0.1/s, sprint 0.35/s, open chest 3, unlock door 4, key pickup 0. Echo Chamber ×2, Hush Room ×0 (player-made noise only; passive drift is unaffected).
- A player's **noise share** = their attributed noise ÷ everyone's, where attributed noise halves every 45s — so the share reflects *recent* loudness.
- Tier effects beyond the banner (patrols, shifts, elites, Warden, Collapse countdown) arrive with enemies (M4) and the run end (M5).

## 7. Cards

### 7.1 Model
- Cards are **one-time abilities**, not movement or attacks.
- Each player has a personal **deck** (starter deck of ~6 cards **[TUNE]**).
- **Hand of 3.** Playing a card discards it. You **draw 1 when entering a room for the first time** (any player's first entry doesn't count — it's per player) and **1 every 30s** **[TUNE]** if your hand isn't full.
- When the deck is empty, the discard reshuffles in.
- Cards are added during the run (Library, Merchant, Shrine, chests, secret rooms). Thinning the deck is possible at Shrines.

### 7.1b Implemented (M3)
- Data in `game/data/cards.json` (rules + card list); effects are composable (`Dash`, `Blink`, `Silence`, `Unlock`, `RevealAdjacent`, `RevealRooms`, `ShiftNoise`, `Steal`, `ClaimRoomLoot`, `MultiplyNextChest`, `GrantPoints`, `ScaleOwnNoise`).
- Hand of 3 in fixed slots (keys: WASD `1 2 3`, Arrows `K L ;` / numpad `1 2 3`, gamepad `X Y B`).
- Starter deck: Dash ×2, Hush, Lantern, Smoke Veil, Lockpick.
- **Hands are secret**: drawn cards are only sent to their owner's machine; the hand *count* and every *played* card are public (the played card's name floats above the player).
- Targets are chosen by the host: a locked door in reach (Lockpick), the player beside you (Pickpocket), or the nearest player anywhere (Scapegoat, Shared Fortune). A card that can't do anything is refused and stays in hand, with a short message to that player.
- **Library**: 3 free cards on lecterns, one per player. **Merchant**: 3 cards for carried points. Gained cards go to the discard pile.
- 16 cards (M4 added Flash Powder, Shatter, Decoy, Rally). Starter deck is now Dash ×2, Hush, Lantern, Flash Powder, Lockpick, Rally.

### 7.2 Card anatomy
`Name · Category · Noise · Rarity · Price · Effect · (Target)`

Categories: **Mobility, Stealth, Utility, Combat, Greed, Social** (Social cards interact with other players — helpful or not).

### 7.3 Starter & example cards (first pass)
| Card | Cat. | Noise | Effect |
|---|---|---|---|
| Dash | Mobility | 1 | Quick burst in move direction, i-frames. |
| Hush | Stealth | 0 | 8s: you generate no noise. |
| Lockpick | Utility | 1 | Open a locked exit/chest without a key. |
| Flash Powder | Combat | 2 | Stun all enemies in the room for 3s. |
| Lantern | Utility | 0 | Reveal adjacent rooms on the map. |
| Rally | Social | 1 | Instantly revive a downed ally in the room. |
| Blink | Mobility | 2 | Teleport to a spot in line of sight. |
| Scapegoat | Social | 0 | Move 10 of your attributed Threat to another player. |
| Pickpocket | Social | 2 | Steal a key or small Points from a nearby player. |
| Loot Magnet | Greed | 2 | Pull all loot in the room to you. |
| Gilded Greed | Greed | 4 | Next chest gives double Points. |
| Smoke Veil | Stealth | 0 | Enemies lose track of you; reduce your Noise share. |
| Void Sight | Utility | 1 | Reveal hidden exits and secret rooms on this floor. |
| Decoy | Stealth | 1 | Place a noisemaker; Strikes and enemies prefer it for 10s. |
| Shatter | Combat | 5 | Kill a non-elite enemy. Very loud. |
| Shared Fortune | Social | 1 | You and an ally each gain Points. |

## 8. Rooms

| Room | Purpose |
|---|---|
| **Lobby** | Start & extraction point. |
| **Corridor** | Connector; patrols pass through. |
| **Vault** | Chests; locked, needs a key or Lockpick. |
| **Guard Post** | Enemies guarding decent loot. |
| **Echo Chamber** | Noise ×2, great loot. |
| **Hush Room** | No noise. Safe-ish place to regroup. |
| **Library** | Pick 1 of 3 cards (each player once). |
| **Merchant** | Buy cards, keys, healing with Points. |
| **Shrine** | Trade a heart / Threat for a powerful card, or remove a card from your deck. |
| **Trap Hall** | Pressure plates, darts; rewards Sneak. |
| **Shifting Room** | Exits rotate at each Threat tier. |
| **Collapsed Passage** | Shortcut that opens/closes with Threat. |
| **Prison** | Free a captive for a reward (noisy), or find a downed teammate's bag. |
| **Void Door** | Secret room behind a Void seal: miniboss + top-tier card. Doors lock until the miniboss is beaten. |
| **Stairs** | Floor transitions. |
| **Relic Chamber** | Relic(s); usually guarded or trapped. |

Room content is built from **hand-made templates** (layout, spawn points, exit sockets) placed by the generator and filled with seeded content — not fully procedural interiors.

## 9. Economy

- **Points**: loot from chests, enemies, Relics. Spent in-run at Merchants/Shrines; banked on extraction; converted to meta currency.
- **Standard Keys**: found or bought. Open locked exits and Vaults. Personal, can be dropped/traded/stolen.
  - *Implemented (M2):* every doorway of a **Vault** is locked. A Vault that would cut other rooms off from the Lobby becomes a Guard Post instead, so exploring never requires a key. The generator places one key per locked Vault + `extraKeys`, only in rooms reachable without keys. Keys are picked up by walking over them; one key opens one doorway (both sides) for everyone.
- **Void Keys** (rare): open Void Doors.
- Spending in-run reduces what you can bank — that's intentional: invest to survive vs. hoard to win.

## 10. Meta-Progression (Hub)

- The **Hub** is also the multiplayer lobby: players walk around, ready up, shop.
- Each player has their **own profile** (saved locally).
- **Passive cards** are bought with meta currency. Prefer **unlocks and sidegrades** over raw stats, to keep runs fair between new and veteran players:
  - Unlock new cards into the in-run pool.
  - Unlock alternate **starter decks** (e.g. "Thief", "Warden-touched", "Scholar").
  - Small conveniences: start with 1 key, +1 card in starting hand.
  - Very limited stat boosts (e.g. +1 heart max, capped).
- **[OPEN]** Should the host be able to toggle "meta perks off" for fair competitive runs?

## 11. Multiplayer

- 1–4 players. **Online** (host + clients) and **local couch** play, mixable (e.g. 2 on one couch + 2 online).
- Solo is a supported, balanced mode (Threat and loot scale with player count **[TUNE]**).
- **Local camera [OPEN]**: recommended — local players share one screen while in the same room; **split-screen** when they're in different rooms.
- Friendly fire: off. Social interference only through **Social cards** and loot-bag stealing.
- Drop-in mid-run: not in v1.

## 12. Scope: Vertical Slice (first playable goal)

- 2–4 players, online + local.
- 1 floor (~6×6), 8 room types (Lobby, Corridor, Vault, Guard Post, Echo, Hush, Merchant, Relic Chamber).
- 3 enemy types (Patrol, Sentry, Warden).
- Shared Threat with tiers 0–5, attribution, Strikes.
- 12 cards.
- Downed / revive / loot bag.
- Extraction, Collapse, scoreboard.
- No meta, placeholder art.

## 13. Open Questions
- Ghost mechanics for dead players?
- Rope Lifts (deep extraction) — yes/no, cost?
- Carry limit / encumbrance (heavy loot slows you)?
- Meta bonus for winning a run?
- Number of floors in the full game, and a final "boss floor"?
- Working title stays *Violet Vaults*?
