# Workflow: add a new card

Input: card idea (name, what it does, category, rarity).

1. **Can existing effects express it?** Effect types and targets are listed at the top of `game/data/cards.json`. If yes, it's data-only: skip to 4.
2. **New effect type:** add it to `EffectType` (`src/VioVau.Core/Cards/CardsConfig.cs`), implement it in `ApplyEffect` (`src/VioVau.Core/RunState.Cards.cs`). New target rules go in the target switch above it. Any new outcome is a new `IGameEvent` (registered with `[JsonDerivedType]`), applied in `RunView` if peers need the state.
3. **Test it** in `src/VioVau.Core.Tests/CardTests.cs` (give the player the card, play it, assert events/state). Also update the effect list comment in `cards.json`.
4. **Data:** add the entry to `game/data/cards.json` (`id`, `name`, optional `shortName` if the name is wider than a hand card, `category`, `rarity`, `noise`, `price`, `text`, `target`, `effects`). Add it to `starterDeck` only if asked.
5. **Visuals:** react to the new event in `GameSession` events code (Fx + `Audio.PlaySfxAt`, must work on clients). Card art: add a 32×32 icon via `tools/generate_card_art.py` + `game/tools/ItemImporter.tscn`, or note it as missing.
6. `tools/check.sh`, then `tools/playtest.sh online --shots` and check the hand card renders (name fits, art visible).
