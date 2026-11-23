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
- Seeded random number generation for reproducible combat and automated tests.
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
| **Weight Tradeoffs** | No hard weight cap; total component mass reduces move speed and increases vulnerability to heavy-target attacks (Titan, Gravity Crush) | Aligns with emergent-tradeoff design philosophy. |
| **Component Categories** | `ComponentCategory` is an organizational label only, never a hard slot restriction | Prevents arbitrary mech-builder slot limits; constraints emerge from Wit, heat, weight, and component rules. |

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

### Damage allocation (defender chooses) [Implemented]
- **Attackers cannot target components.** The defender decides where damage is applied.
- Each Crest has a **Damage Order**, set in the Workshop, listing its components in the order they absorb penetrating damage.
- Each penetrating point goes to the first operational component in the Damage Order. When a component is destroyed it can no longer take damage, and remaining points flow to the next component in order.
- A Crest is Defeated when its Core is no longer operational.

### Defenses vs effects
- Protection rolls, Shield Generators, and Aegis save against **damage only**, never against a hit's effects (push/pull, interruption, disruption). A fully saved hit still interrupts.
- Exception: future components may be designed to protect against specific effects.
- Blink Drive evasion avoids the attack entirely (evasion, not defense).

### Impact window & reactions [Partially implemented: impact window, reactions resolve first; pending: reactions gated to incoming impacts, reaction rules, slow motion]
- An action's Executing phase is its **impact window**. Damage and effects resolve at the end of the impact window, not at the end of windup.
- Reactive actions (Blink, Repulsor, Aegis, etc.) are valid only **during the impact of an incoming attack** against the reacting Crest.
- Digital play supports reactions two ways, combined: **pre-set reaction rules** configured on the Crest (condition → reaction, e.g. "incoming impact ≥ 2 damage → Blink"), and a **slow-motion window** during incoming impacts for manual reactions. Slow motion is presentation-only and does not affect simulation determinism.

### Concurrent actions (Wit-gated)
- Each component can run **one action at a time**.
- Multiple components can run actions simultaneously as long as the Crest has enough available Wit for every committed action. Every action reserves Wit while active.
- Carrying more equipment raises passive Wit Tax, which reduces how many actions can run at once — the core cost of over-equipping.

### Interruption with concurrent actions
- Interruptibility is a property of each action's stats. Some actions cannot be interrupted; others can (with their own resistance).
- A successful interruption stops **all interruptible actions currently in motion** on the Crest. Uninterruptible actions continue.

### Command buffering [Implemented]
- Commands issued mid-beat are buffered and take effect at the next beat boundary.

### Damage Order guidance [Implemented]
- The Core may be placed anywhere in the Damage Order, but the Workshop warns when it is not last.

### Trait checks
- Trait-scaled bonuses check the target's **current state**, not the allocation: e.g., Acid's bonus applies if the target has any currently operational armor, regardless of which component its Damage Order sends the damage to.

### Movement from mobility components
- Movement is provided by mobility components, each with its own movement statistics and costs. Some legs move freely; others require Wit commitments. Mobility components may grant extra movement options (e.g., Dash).

### Weight & trait-scaled damage
- Heaviness is punishable but never unplayable: weight reduces mobility on a gentle curve with no hard floor doing the balancing.
- Damage types scale against target traits with **additional damage**: e.g., Gravity deals extra to heavy targets; Acid deals extra to armored targets.

### Combat style
- Live-action tactical combat with optional pause (in the spirit of real-time-with-pause party RPGs).

### Persistence (scope)
- Repairs and restocking can be done in the field, so in the full game, damage persisting between fights costs time. **For the vertical slice**, Crests are fully repaired and restocked between encounters.

### Next vertical slice: Workshop & Research
- Exotic materials are discovered and researched. Once researched, they unlock new items that can be produced from **fast-iron**.
- **Fast-iron** is created by the **Square Summon** spell, which costs **4 Motes of Moment**. A Crest that cannot store at least 4 Motes cannot produce fast-iron in the field.
- Square Summon can be cast mid-combat, but it is slow unless the Crest has additional Mote generation.
- In the Workshop, supply is effectively unlimited because time is not a factor.

---

## 4. Deferred Long-Term Concepts (Preserved in Architecture)

- Inventor's Workshop (Forge, Workbench, Research Desk, Crest Bays, Spell Lab, Storage). **Next slice** — see 3a.
- Discovery, reverse engineering, and research trees.
- Fast-iron, Square Summon, and Shaping spells.
- Multiple simultaneous Crests / autonomous constructs (**Grip**).
- Extradimensional storage and Mote-to-Wit conversion engines.
- Exploration as engineering (environmental puzzles using combat spells).
- Shared tabletop rules runner (physical dice, driving the same beat engine).
- Generative AI content operating within deterministic rules validation.

---

## 5. Known Limitations (Vertical Slice)

- Single player-controlled Crest per encounter (architecture supports multiple friendly combatants).
- Single test encounter with 3 enemy archetypes (Skirmisher, Bruiser, Artillery); repeatable with any custom Crest assembly.
- Saved configurations live in session memory (JSON persistence can be added directly on top of `CrestConfiguration`).
- Functional vector/shape graphics in WPF rather than final art assets.
