# Tools and Tomes — Living Design Document

This document tracks confirmed game rules, implementation decisions, open questions, deferred long-term concepts, and known limitations for the **Combat Vertical Slice**.

---

## 1. Foundational Architecture Principle: Component-Based Crests

> **There are no chassis or predefined Crest archetypes in Tools and Tomes. A Crest is not a type of machine that receives components — a Crest is the machine that emerges from its components.**

- A Crest (`CrestLoadout`) is simply a named collection of installed `EquipmentInstance`s.
- There is no root chassis, no predefined Crest classes (no Strider/Bulwark/Artificer/Devastator types), and no hardcoded chassis-specific equipment slots.
- **All Crest capabilities are derived purely from installed operational components** (`LoadoutRules.ComputeStats`):
  - **Cores & Engines** (e.g., *Aether Dynamo Core*, *Heavy Aether Furnace*) contribute Wit capacity bonuses, Mote storage, Mote generation rate, heat capacity, dissipation, and structural resistance — they do not define an archetype.
  - **Mobility Actuators** (e.g., *Articulated Stride Legs*, *Reinforced Anchor Legs*, *Jump Jets*) determine movement speed and displacement resistance; disabling them degrades mobility.
  - **Armor, Weapons, Defenses, Arcane & Utility components** contribute protection dice, actions, Wit taxes, weight, and thermal properties.
- Players can name, save, restore, and modify configurations (`CrestConfiguration`) and transfer individual component instances (preserving durability and ammo) between assemblies or spare storage.
- The two starter assemblies (*Agile Skirmish Assembly* and *Heavy Anchor Assembly*) are purely illustrative saved component lists demonstrating contrasting emergent builds.

---

## 2. Confirmed Game Rules

### Core Philosophy
- Real-time tactical simulation with pause/resume.
- Deterministic, beat-based simulation: **1 beat = 1 tabletop second**. All rules resolve on beat boundaries; the real-time client only interpolates presentation between beats.
- Seeded random number generation (PCG32, fully specified in code) for combat that reproduces identically across .NET versions, replays, and tests.
- Unified action/timeline model shared by weapons, spells, defensive reactions, and enemy AI.
- Equipment instances are distinct from equipment definitions: damage and ammo consumption persist across unequip, transfer, re-equip, and encounters until repaired or replaced.

### Resources: Wit & Motes of Moment
- **Wit** (magical capacity):
  - Pilot base capacity: **10** with an operational Spellbook, **5** without, plus any `BonusWitCapacity` from operational components (such as a *Heavy Aether Furnace*).
  - Installed operational equipment imposes a passive **Wit Tax**.
  - Damaged/disabled or removed equipment immediately releases its Wit Tax (and stops providing any Wit capacity bonus).
  - Active spells and Wit-costing abilities **Reserve** Wit while winding up, executing, or sustaining an effect (e.g., Aegis barrier), releasing it upon completion, expiration, or interruption.
  - `AvailableWit = MaxWit - ActiveEquipmentWitTax - ReservedWit`.
- **Motes of Moment** (consumable magical resource):
  - Base generation: **1 Mote every 5 beats**, shortened by operational components with `MoteIntervalReductionBeats` (e.g., *Heavy Aether Furnace* −1 → every 4 beats).
  - Default storage cap: **3 Motes** + bonuses from operational components (e.g., +2 from Spellbook, +1 from Heavy Aether Furnace).
  - Consumed by Mote-costing weapons (e.g., Titan Hammer) and spells (e.g., Gravity Crush, Aegis).

### Damage, Protection & Component-Only Defeat Model
- Damage is represented in whole numbers.
- **Protection Rolls (d6):**
  - Uniform base save target: **5+** on a d6.
  - **1 always fails** (hits); **6 always saves**, regardless of modifiers.
  - Each incoming point of damage receives 1 base protection roll plus additional d6 rolls from operational defensive components (e.g., Heavy Armor Plating, Shield Generator). A point of damage is saved if any protection roll succeeds.
- **Component-Only Damage:**
  - Every penetrating point of damage hits an installed component (reducing its durability by 1).
  - When a component reaches 0 durability, it is **Disabled**: its functionality stops, passive bonuses stop applying, and its Wit Tax is immediately released.
  - Each penetrating point hits the first operational component in the defender's **Damage Order**; the Core takes damage when its turn in the order arrives (recommended: last).
  - When a Crest has no operational **Core**, the Crest is **Defeated**.

### Defensive Systems & Spells
- **Shield Generator:** Passive Wit-taxed component granting bonus d6 protection rolls while operational; can be damaged/disabled.
- **Repulsor:** Short-range reactive attack (1 damage + pushback) for breaking melee engagement and interrupting melee windups.
- **Blink Drive:** Reactive short-range teleportation; if triggered within its valid reaction window, completely evades incoming committed attacks.
- **Aegis (Spell):** Active spell that reserves Wit while active (plus Mote cost) to erect a timed protective barrier absorbing a fixed number of incoming hits—tactically distinct from passive Shield Generator dice.
- **Push / Pull (Spell):** Low-windup displacement spell to reposition combatants or disrupt windups.
- **Gravity Crush (Spell):** Committed control/damage spell; deals bonus impact against heavy Crests and disrupts actions.
- **Dragon's Breath (Weapon):** Short-range ammo-consuming weapon dealing damage and generating **Heat** on the firing Crest. Operational Thermal Regulators dissipate heat; overheating incurs recovery penalties.

### Interruptions
- Data-driven interruption power vs. action interruption resistance distinguishes three tiers:
  - **Disruption:** Delays the current action's windup/execution timer.
  - **Interruption:** Forces the current action to stop and enter the `Interrupted` state (releasing reserved Wit).
  - **Cancellation:** Prevents the action from resolving and applies full recovery penalty.

---

## 3. Implementation Decisions & Tradeoffs

| Area | Decision | Rationale |
|---|---|---|
| **Presentation Host** | WPF Desktop App (`net10.0-windows`) | Native Windows executable with direct in-process binding to `ToolsAndTomes.Core`. |
| **Beat Engine** | Simulation advances only in whole beats (`CombatSimulation.StepBeat`); the WPF client steps one beat per wall-clock second and interpolates movement and timers between beats | The digital game and the tabletop game run the exact same rules; nothing depends on sub-second timing. |
| **Per-Actor Resolution Reconciliation** | End-of-beat resolution batches all impacts per actor (sorted by `CombatantId`) | Preserves tabletop per-actor resolution semantics in real time without artificial delays or animation-order bias. |
| **Hit Allocation** | Defender-chosen Damage Order: the installed component list order, set in the Workshop; each penetrating point hits the first operational component; no attacker targeting | Makes build order a meaningful defensive decision that works identically at the table. |
| **Weight Tradeoffs** | No hard weight cap; weight beyond the operational legs' load capacity costs 1 movement per 40 over (minimum 1), and heavy Crests take trait bonus damage (Titan, Gravity Crush) | Aligns with emergent-tradeoff design philosophy. |
| **Component Categories** | `ComponentCategory` is an organizational label only, never a hard slot restriction | Prevents arbitrary mech-builder slot limits; constraints emerge from Wit, heat, weight, and component rules. |
| **Content as Data** | Actions, components, starter builds, enemies, and global rules live in `src/ToolsAndTomes.Core/Content/*.json` (embedded), loaded by `ContentSerializer` and checked by `ContentValidator`; `LoadFromDirectory` swaps in edited content | Content can be balanced, modded, or generated without code changes, and nothing unplayable gets in. |
| **Rules Config** | All global tuning numbers live in `RulesConfig` (`rules.json`) | One place to tune; alternative rule sets can be tested side by side. |
| **Balance Runner** | `BalanceRunner` plays every starter build against the test encounter across many seeds (player on a build-agnostic `AutoPilot`, enemies on `EnemyAIController`); `tools/ToolsAndTomes.Balance` prints win rate, fight length, components lost, and kills, optionally for an edited content directory | Balance changes are measured, not guessed. |

---

## 3a. Confirmed Design Direction (Nov 2026 review)

These decisions are confirmed and **supersede** any conflicting rule above. Subsections marked **[Implemented]** are reflected in code; the rest are pending.

### Dual-medium parity [Implemented]
- The game is designed **equally** for digital play and tabletop play. Every rule must be executable by hand at a table.
- All action timings are authored in **whole tabletop seconds ("beats")**. The real-time engine may subdivide beats for presentation and movement smoothness, but phase transitions and damage resolution happen on beat boundaries.
- Rules quantities (damage, durability, Wit, Motes, heat, weight, movement, distances, displacement resistance) are small integers trackable with dice and tokens. Positions may be fractional (measured with a tape at the table).
- Phases may be 0 beats long (reactions have no windup; light weapons may skip recovery), but every action has an impact window of at least 1 beat.
- Cooldowns count beats **after** recovery ends.

### Beat resolution order [Implemented]
Each beat resolves in this order (simultaneous within each step; ties ordered by combatant id):
1. **Passive timers** — Mote generation, heat dissipation, Aegis and evasion countdowns, interruption stun.
2. **Reactions** declared during this beat resolve, then their hits resolve (so a reaction can cancel an attack in its final impact beat).
3. **Actions in motion** consume the beat. Impact windows ending this beat resolve their effects (damage resolves at the **end** of the impact window).
4. **Impact hits** resolve per target: protection rolls, component damage, displacement, interruption.
5. **Movement** — moving Crests advance up to their speed in whole units.
6. **Buffered commands** take effect, starting new actions in the next beat.
7. **Cooldowns** count down.

After each hit-resolution pass, a Crest whose available Wit fell below zero (a Wit-granting component was destroyed) suffers **Wit collapse**: its newest Wit commitments drop (actions, then Aegis, then paid movement) until the budget balances.

`TabletopTranscript` renders the event log beat by beat; a hand-resolved fight is kept as a regression test (`TabletopParityTests`).

### Damage allocation (defender chooses) [Implemented]
- **Attackers cannot target components.** The defender decides where damage is applied.
- Each Crest has a **Damage Order**, set in the Workshop, listing its components in the order they absorb penetrating damage.
- Each penetrating point goes to the first operational component in the Damage Order. When a component is destroyed it can no longer take damage, and remaining points flow to the next component in order.
- A Crest is Defeated when its Core is no longer operational.

### Defenses vs effects [Implemented]
- Protection rolls, Shield Generators, and Aegis save against **damage only**, never against a hit's effects (push/pull, interruption, disruption). A fully saved hit still interrupts.
- Exception: future components may be designed to protect against specific effects.
- Blink Drive evasion avoids the attack entirely (evasion, not defense).

### Impact window & reactions [Implemented]
- An action's Executing phase is its **impact window**. Damage and effects resolve at the end of the impact window, not at the end of windup.
- Reactive actions (Blink, Repulsor, Aegis, etc.) are valid only **during the impact of an incoming attack** against the reacting Crest.
- Digital play supports reactions two ways, combined: **pre-set reaction rules** configured on the Crest (condition → reaction, e.g. "incoming impact ≥ 2 damage → Blink"), and a **slow-motion window** during incoming impacts for manual reactions. Slow motion is presentation-only and does not affect simulation determinism.
- Reaction rules are stored on the Crest (and in saved configurations) as `ReactionRule` entries: reaction action, optional telegraph kind, and minimum incoming damage. At the start of the reaction step, a Crest that declared no reaction checks its rules in order and declares the first that matches and is legal. A manually declared reaction takes precedence. Enemy AI can use the same mechanism.
- Slow motion: the WPF client stretches a beat to 3× wall-clock time whenever the player has a legal reaction available (toggleable).

### Concurrent actions (Wit-gated) [Implemented]
- Each component can run **one action at a time**.
- Multiple components can run actions simultaneously as long as the Crest has enough available Wit for every committed action. Every action reserves Wit while active.
- Carrying more equipment raises passive Wit Tax, which reduces how many actions can run at once — the core cost of over-equipping.

### Interruption with concurrent actions [Implemented]
- Interruptibility is a property of each action's stats. Some actions cannot be interrupted; others can (with their own resistance).
- A successful interruption stops **all interruptible actions currently in motion** on the Crest. Uninterruptible actions continue.
- Each interrupted action's component is locked out for the penalty (Interruption: 1 beat; Cancellation: max(2, recovery)). Other components keep working.

### Command buffering [Implemented]
- Commands issued mid-beat are buffered and take effect at the next beat boundary.

### Damage Order guidance [Implemented]
- The Core may be placed anywhere in the Damage Order, but the Workshop warns when it is not last.

### Trait checks [Implemented]
- Trait-scaled bonuses check the target's **current state**, not the allocation: e.g., Acid's bonus applies if the target has any currently operational armor, regardless of which component its Damage Order sends the damage to.

### Movement from mobility components [Implemented]
- Movement is provided by mobility components, each with its own movement statistics and costs. Some legs move freely; others require Wit commitments. Mobility components may grant extra movement options (e.g., Dash).
- Walking requires an operational locomotion component (`IsLocomotion`), reserves its `MovementWitCost` while walking, and runs alongside other actions. Actions out of range approach automatically using the legs.
- Jump Jets grant **Jet Dash** (0/1/1, 4 units, 1 Wit, uninterruptible).

### Weight & trait-scaled damage [Implemented]
- Heaviness is punishable but never unplayable: weight reduces mobility on a gentle curve with no hard floor doing the balancing.
- Damage types scale against target traits with **additional damage**: e.g., Gravity deals extra to heavy targets; Acid deals extra to armored targets.
- Implemented as `TraitBonus` entries on actions (`Heavy` with a weight threshold, `Armored`). Acid Spray (+1 vs Armored) comes from the new Acid Sprayer component.
- Locomotion has a `LoadCapacity` (Stride Legs 70, Anchor Legs 150); heavy builds stay mobile by fitting sturdier legs.

### Combat style
- Live-action tactical combat with optional pause (in the spirit of real-time-with-pause party RPGs).

### Persistence (scope) [Implemented]
- Damage persists within a campaign mission; the Workshop repairs and restocks only before a mission starts. Between encounters, repairs happen in the field (see Campaign missions).

### Workshop & Research slice [Implemented]
- Exotic materials are discovered and researched. Once researched, they unlock new items that can be produced from **fast-iron**.
- **Fast-iron** is created by the **Square Summon** spell, which costs **4 Motes of Moment**. A Crest that cannot store at least 4 Motes cannot produce fast-iron in the field.
- Square Summon can be cast mid-combat, but it is slow unless the Crest has additional Mote generation.
- In the Workshop, supply is effectively unlimited because time is not a factor.
- **Discovery:** winning an encounter grants material samples from its reward table (`encounters.json`): guaranteed samples plus weighted draws, rolled from the encounter seed on a separate stream from the combat dice.
- **Research:** each material (`materials.json`) needs `samplesToResearch` samples; researching consumes them and permanently unlocks its blueprints.
- **Known blueprints:** only components in the starter builds are known at first; every other component must be unlocked by some material (enforced by `ContentValidator`).
- **Fabrication:** known blueprints fabricate from fast-iron for free in the Workshop.
- Shipped materials: Oathsteel (Oathsteel Plating, Righteous Strike), Corrodite (Acid Sprayer), Bastion Ore (Siege Mortar, Falconet), Stormglass (Stormglass Capacitor), Skyweave (Skystrider Legs: speed 7, load 90, 1 Wit to walk).
- The Research Desk screen shows samples, research status, unlocks, and known blueprints; the hangar fabricates only known blueprints.

### Cargo & field shaping [Implemented]
- A **Cargo Rack** component carries prepared items: fast-iron and consumables (4 cargo units; carried items add weight). Its contents are unusable while it is disabled.
- Each assembly has a **cargo manifest** set in the Workshop; cargo is restocked to it before every encounter (Workshop supply is free).
- **Consumables** (`consumables.json`): Ammo Pack (refill one weapon), Shaped Charge (thrown, 2 damage), Iron Ward (reaction, absorbs one hit). Each is used through a cargo-rack action and consumed.
- **Shape Fast-iron** (Spellbook spell, 1/1/1, 1 Wit, interruptible) turns fast-iron into **anything**: a component (any known blueprint), a consumable into cargo, or a weapon's ammunition. Fast-iron is spent from loose stock (Square Summon) first, then cargo, when the impact window opens, so an interrupted windup spends nothing.
- **Costs are per recipe in content:** components use `fastIronCost`, else ceil(weight / 10); consumables use `fastIronCost`; ammo refills use the weapon's `ammoRefillFastIronCost`.
- **Anything shaped in the field joins the very end of the Damage Order** (after the Core).
- A Crest without Square Summon can still shape by carrying fast-iron in. Deferred: carrying ready-made spare components and installing them mid-fight.


### Campaign missions [Implemented]
- The loop is: **Workshop → Mission Select → encounter → rest in the field → encounter → rest → encounter → Workshop.**
- A mission (`missions.json`) is an ordered list of encounters with a rest budget (beats) between them. The same Crest carries through: damage, ammo, cargo, shaped parts, Motes, and loose fast-iron persist; heat, cooldowns, and activity reset each fight.
- **Rest in the field:** the time budget passes one Mote interval at a time (+1 Mote each). Square Summon, repairs, shaping, and cargo use happen between Mote generations and cost no time. Field repair: 1 fast-iron restores `repairPointsPerFastIron` (2) durability and revives disabled parts. Unused rest time is lost.
- **Rewards** bank as each encounter is won (granted to the Workshop immediately); finishing grants the mission's completion bonus. **Defeat fails the mission but keeps banked samples.** The player can withdraw during a rest (or abandon a fight) keeping banked samples.
- **Missions unlock by completing earlier missions** (`requiredMissions`, tracked in the profile) **and by research** (`requiredResearch`). The validator rejects unknown ids and cycles.
- Enemies with reaction rules: Oathbound Warden (repulses melee), Skyblade Raider (blinks from heavy impacts).
- **Bosses** use the `Warlord` archetype. Ordinary enemies issue one command when idle. A Warlord commits every attack its Wit allows at its target each beat, as the player's autopilot does, so a boss's extra weapons add pressure, not just options. At the table: "each beat, the Warlord starts every attack it can afford."

### Demo region: The Border March [Implemented]
The free demo's region (`region: "border_march"`), played in order with the lance of three:

| Mission | Encounters | Role |
|---|---|---|
| **First Patrol** | Outriders (2 Skirmishers) → Roadblock (Bruiser + 2 Skirmishers) | Tutorial: read windups, answer hits |
| **Border Patrol** | Scout Screen (2 Skirmishers + Skyblade) → Iron Column (2 Bruisers + Artillery) → Border Fort (Warden, Bruiser, Skirmisher, Artillery) | The attrition mission: damage carries over |
| **The Warlord's Gate** | Gatehouse (Warden, Bruiser, Artillery) → The Warlord's Gate (**Gatewarden Kastor** + Skyblade + Bruiser) | Finale and boss |

- Gatewarden Kastor: heavy plate, Repulsor (reacts to melee), Grimoire, Falconet, Righteous, Titan, anchor legs, and an Aether Furnace.
- Quarry Siege and Storm Reach come after the demo: they need the finale completed, plus their research. They are **not yet tuned for the lance**: today the lance completes them 99–100% of the time.
- The old single-mission encounters (Scout Pair, Bruiser Escort, Lone Bruiser) are retired. The Proving Grounds stays as the balance runner's default single fight.
- **Balance** (autopilot, starter builds Agile / Heavy, full lance, 200 seeds). Run `tools/ToolsAndTomes.Balance -- --missions --allies all` to re-measure.

  | Mission | Agile | Heavy | Average | Target | Beats per fight |
  |---|---|---|---|---|---|
  | First Patrol | 98% | 100% | 99% | ~90%+ | 56–61 |
  | Border Patrol | 74% | 54% | 64% | 60–70% | 85–119 |
  | The Warlord's Gate | 54% | 34% | 44% | ~40% | 90 |

  - Heavy Anchor is slow: about 12% of its Border Patrol runs hit the 300-beat limit.
  - **Solo, the demo is nearly unwinnable** (0–2% completion). The demo is designed around the lance.
- **Mid-mission saves [Implemented]:** the mission in progress is saved inside the profile (`activeMission`, a `MissionSave`).
  - It is saved only **between fights**: at mission start, just before each encounter begins, and after every rest action.
  - Quitting mid-fight resumes **at the start of that fight**, with the lance as it left the last rest. The encounter's seed is fixed, so the fight replays identically.
  - Each Crest is saved in full: Damage Order including field-shaped parts, durability, ammo, cargo, Motes, and loose fast-iron. The mission saves its stage, rest time, banked rewards, and rest log.
  - On launch, the app resumes at the rest or at the next fight. The resumed Crest replaces the assembly it launched with.
  - A save that no longer matches the content (a removed mission, pilot, or component) is abandoned with a warning; banked samples are kept.

### Allied pilots [Implemented]
- **The player pilots one Crest; up to two allied Crests (a lance of three) fight alongside under AI control.** Allies take no orders.
- **Named pilots** (`pilots.json`): each has a name, a callsign, a bio, and a starting assembly. The roster persists in the profile: each pilot's Crest can be refit, and the lance (who comes along, in order) is saved. Today the demo has two pilots: **Anvil** (Hollis Marrow, a heavy Repulsor guard) and **Kestrel** (Sera Vell, a fast Falconet/Talon scout).
- **In a fight:**
  - Allies are Player-faction Crests with ids `ally_<pilotId>`. They start at the encounter's `allyStarts`, or flanking the player's start when none are given.
  - **The fight is lost when the player's own Crest falls**, even with allies standing. Losing allies doesn't lose it.
  - Ally AI (`AllyAIController`) fights like the build-agnostic `AutoPilot`, but targets the enemy closest to both itself and the player (distance to self + ½ × distance to the player's Crest), so the lance converges.
  - Enemy AI targets the nearest lance member, and keeps its current target unless another Crest is more than 3 tiles closer.
- **Across a mission:** ally damage, Motes, and fast-iron carry through like the player's.
  - An ally whose Core falls sits out later fights until its Core is repaired at a rest.
  - At a rest, waiting gives every standing Crest a Mote. Each Crest summons, repairs, shapes, and uses cargo with its own Motes and fast-iron (`crestId` on the `MissionRun` rest methods; a Crest picker on the rest screen).
  - **Fast-iron can be handed between Crests** at a rest (free, and it takes no time). Allies have no Square Summon, so the player's summoned fast-iron is what keeps the lance repaired. Spending it on yourself or your allies is a real choice. This one rule took Border Patrol from 25–43% to 55–80% completion.
  - The Workshop repairs every Crest before each mission.
- **At the table:** the ally policy is simple enough to run by hand: a weighted-nearest target, then the strongest affordable attacks first. Writing it up as table rules, alongside the enemy AI's rules, is still to do.
- The balance runner reports allies down at the end of each mission and the average beats per fight (`--allies <ids|all>`).

### Next: make the campaign playable [Planned]
- **Direction (decided Nov 2026):**
  - Make it fun before generating real assets.
  - Ship a **free Steam demo** (the first region, 3 missions, about 1 hour, one biome), then Early Access.
  - Use a **mid-distance third-person camera**, so Crests must read as detailed machines up close.
- ✅ **Demo region tuned** (see *Demo region* above). Targets, measured with the full lance and the autopilot (which plays worse than a human with pause): First Patrol ~90%+, Border Patrol ~60–70%, The Warlord's Gate ~40%. Fights last about 1–2 minutes of real time (60–120 beats).
- Still to do: retune Quarry Siege (~30–40%) and Storm Reach (~10–20%) for the lance. These are post-demo, Early Access content.
- ✅ **Mid-mission saves** (see *Campaign missions*).
- Then a hands-on polish pass of the hangar, combat, rest, mission select, and research screens.

---

## 4. Deferred Long-Term Concepts (Preserved in Architecture)

- Inventor's Workshop beyond the Research Desk (Forge, Workbench, Crest Bays, Spell Lab, Storage). The Research Desk slice is implemented — see 3a.
- Discovery, reverse engineering, and research trees.
- Fast-iron, Square Summon, and Shaping spells.
- Multiple simultaneous Crests / autonomous constructs (**Grip**).
- Extradimensional storage and Mote-to-Wit conversion engines.
- Exploration as engineering (environmental puzzles using combat spells).
- Shared tabletop rules runner (physical dice, driving the same beat engine).
- Generative AI content operating within deterministic rules validation.

---

## 5. Known Limitations (Vertical Slice)

- One player-controlled Crest per encounter; up to two AI-controlled allied pilots (no orders yet).
- Five campaign missions (the 3-mission demo region plus 2 later ones) and six enemy types, including one boss. The Proving Grounds encounter remains the single-fight test case for the balance runner.
- Player progress (assemblies, spares, research, samples) is saved to `%LOCALAPPDATA%\ToolsAndTomes\profile.json`; component damage is not saved (the Workshop repairs everything).
- Functional vector/shape graphics in WPF rather than final art assets.

---

## 6. Product Direction & Roadmap (Nov 2026)

**Goal:** an AA video game for **PC (Steam)**, built in **Godot 4 (C#)** with **3D Crests and arenas and a tactical camera** (real-time with pause). `ToolsAndTomes.Core` stays the engine-free rules engine; the WPF app remains a debugging and balancing tool. The **tabletop game is a separate companion product**: rules parity stays a design constraint, and it ships separately.

| Phase | Goal | Key work |
|---|---|---|
| 0. Finish the current loop | A fun, playable 3-mission demo region | Allied pilots ✅; the demo region tuned with the lance ✅; fights of 60–120 beats ✅; mid-mission saves ✅; a feel pass for the close camera. |
| 1. Godot vertical slice | One encounter at target quality | Confirm Godot's .NET version can reference Core (multi-target Core if needed); Core↔Godot bridge (beat events + snapshots, presentation interpolates); tactical camera; one modular 3D Crest assembled from component models; telegraph VFX; audio hooks; controller-ready input. |
| 2. Core hardening | Production-ready rules engine | Full save/load (including mid-fight); a stable presentation API; asset ids on content (model, VFX, sound per component/action); localization keys; performance budgets. |
| 3. Content & tools pipeline | Designers add content without code | Editors for components, actions, encounters, missions over the JSON + validator; balance runner in CI. |
| 4. Systems breadth | Full game scope | Workshop rooms, Grip (multiple Crests), exploration, world map/narrative, progression depth. |
| 5. Production | AA release on Steam | Art direction, animation, audio, UX, accessibility, QA, Steam integration. |

**Art and audio:** generated by self-hosted, commercially licensed AI models with human review, in a toon / cel-shaded style. See [ASSET_PIPELINES.md](ASSET_PIPELINES.md).
