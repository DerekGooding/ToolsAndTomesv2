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
- Deterministic, fixed-step simulation (`50 ms` per tick = 20 ticks/second) independent of rendering/animation.
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
  - Base generation: **1 Mote every 5.0 seconds** (100 simulation ticks), accelerated by operational components with `MoteGenerationRateBonus` (e.g., *Heavy Aether Furnace* +25% → 4.0s).
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
  - Once all outer operational components are disabled (or if an attacker explicitly targets Core), penetrating hits damage the Crest's **Core**.
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
| **Tick Rate** | 20 Hz (`50 ms` fixed tick) | Smooth real-time readability, zero floating-point drift, clean 20:1 mapping to future 1-second tabletop ticks. |
| **Per-Actor Resolution Reconciliation** | End-of-tick `ResolutionPhase` batches all impacts per actor (sorted by `CombatantId`) | Preserves tabletop per-actor resolution semantics in real time without artificial delays or animation-order bias. |
| **Hit Allocation** | Default penetrating hits roll among operational non-core components (armor weighted 2x), falling through to Core when no outer components remain; explicit component targeting supported | Makes armor plating meaningfully protect subsystems and Core while supporting tactical targeting. |
| **Weight Tradeoffs** | No hard weight cap; total component mass reduces move speed and increases vulnerability to heavy-target attacks (Titan, Gravity Crush) | Aligns with emergent-tradeoff design philosophy. |
| **Component Categories** | `ComponentCategory` is an organizational label only, never a hard slot restriction | Prevents arbitrary mech-builder slot limits; constraints emerge from Wit, heat, weight, and component rules. |

---

## 4. Deferred Long-Term Concepts (Preserved in Architecture)

- Inventor's Workshop (Forge, Workbench, Research Desk, Crest Bays, Spell Lab, Storage).
- Discovery, reverse engineering, and research trees.
- Fast-iron, Square Summon, and Shaping spells.
- Multiple simultaneous Crests / autonomous constructs (**Grip**).
- Extradimensional storage and Mote-to-Wit conversion engines.
- Exploration as engineering (environmental puzzles using combat spells).
- Shared tabletop rules runner (1-second ticks, physical dice).
- Generative AI content operating within deterministic rules validation.

---

## 5. Known Limitations (Vertical Slice)

- Single player-controlled Crest per encounter (architecture supports multiple friendly combatants).
- Single test encounter with 3 enemy archetypes (Skirmisher, Bruiser, Artillery); repeatable with any custom Crest assembly.
- Saved configurations live in session memory (JSON persistence can be added directly on top of `CrestConfiguration`).
- Functional vector/shape graphics in WPF rather than final art assets.
