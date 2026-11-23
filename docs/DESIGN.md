# Tools and Tomes — Living Design Document

This document tracks confirmed game rules, implementation decisions, open questions, deferred long-term concepts, and known limitations for the **Combat Vertical Slice**.

---

## 1. Confirmed Game Rules

### Core Philosophy
- Real-time tactical simulation with pause/resume.
- Deterministic, fixed-step simulation (`50 ms` per tick = 20 ticks/second) independent of rendering/animation.
- Seeded random number generation for reproducible combat and automated tests.
- Unified action/timeline model shared by weapons, spells, defensive reactions, and enemy AI.
- Equipment instances are distinct from equipment definitions: damage and ammo consumption persist across unequip/re-equip and between encounters until repaired or replaced.

### Resources: Wit & Motes of Moment
- **Wit** (magical capacity):
  - Base capacity: **10** with a Spellbook, **5** without a Spellbook.
  - Installed operational equipment imposes a passive **Wit Tax**.
  - Damaged/disabled or removed equipment immediately releases its Wit Tax.
  - Active spells and Wit-costing abilities **Reserve** Wit while winding up, executing, or sustaining an effect (e.g., Aegis barrier), releasing it upon completion, expiration, or interruption.
  - `AvailableWit = MaxWit - ActiveEquipmentWitTax - ReservedWit`.
- **Motes of Moment** (consumable magical resource):
  - Generates **1 Mote every 5.0 seconds** (100 simulation ticks).
  - Default storage cap: **3 Motes** (+2 bonus storage when equipped with a Spellbook, total 5).
  - Consumed by Motes-costing weapons (e.g., Titan) and spells (e.g., Gravity Crush, Aegis).

### Damage, Protection & Component-Only Defeat Model
- Damage is represented in whole numbers.
- **Protection Rolls (d6):**
  - Uniform base save target: **5+** on a d6.
  - **1 always fails** (hits); **6 always saves**, regardless of modifiers.
  - Each incoming point of damage receives 1 base protection roll plus additional d6 rolls from operational defensive equipment (e.g., Heavy Armor Plating, Shield Generator). A point of damage is saved if any protection roll succeeds.
- **Component-Only Damage:**
  - Every penetrating point of damage hits an installed component (reducing its durability by 1).
  - When a component reaches 0 durability, it is **Disabled**: its functionality stops, passive bonuses stop applying, and its Wit Tax is immediately released.
  - Once all outer operational components are disabled (or if an attacker explicitly targets Core), penetrating hits damage the Crest's **Core**.
  - When a Crest's **Core** is disabled (0 durability), the Crest is **Defeated**.

### Defensive Systems & Spells
- **Shield Generator:** Passive Wit-taxed equipment granting bonus d6 protection rolls while operational; can be damaged/disabled.
- **Repulsor:** Short-range reactive attack (1 damage + pushback) for breaking melee engagement and interrupting melee windups.
- **Blink Drive:** Reactive short-range teleportation; if triggered within its valid reaction window, completely evades incoming committed attacks.
- **Aegis (Spell):** Active spell that reserves Wit while active (plus Mote cost) to erect a timed protective barrier absorbing a fixed number of incoming hits—tactically distinct from passive Shield Generator dice.
- **Push / Pull (Spell):** Low-windup displacement spell to reposition combatants or disrupt windups.
- **Gravity Crush (Spell):** Committed control/damage spell; deals bonus impact against heavy Crests and disrupts actions.
- **Dragon's Breath (Weapon):** Short-range ammo-consuming weapon dealing 1 damage and generating **Heat** on the firing Crest. Operational Thermal Regulators dissipate heat; overheating (especially with a damaged Thermal Regulator) incurs recovery penalties and thermal strain.

### Interruptions
- Data-driven interruption power vs. action interruption resistance distinguishes three tiers:
  - **Disruption:** Delays the current action's windup/execution timer.
  - **Interruption:** Forces the current action to stop and enter the `Interrupted` state (releasing reserved Wit).
  - **Cancellation:** Prevents the action from resolving and applies full recovery penalty.

---

## 2. Implementation Decisions & Tradeoffs

| Area | Decision | Rationale |
|---|---|---|
| **Presentation Host** | WPF Desktop App (`net10.0-windows`) | Native Windows executable with direct in-process binding to `ToolsAndTomes.Core`. |
| **Tick Rate** | 20 Hz (`50 ms` fixed tick) | Smooth real-time readability, zero floating-point drift, clean 20:1 mapping to future 1-second tabletop ticks. |
| **Per-Actor Resolution Reconciliation** | End-of-tick `ResolutionPhase` batches all impacts per actor (sorted by `CombatantId`) | Preserves tabletop per-actor resolution semantics in real time without artificial delays or animation-order bias. |
| **Hit Allocation** | Default penetrating hits roll uniformly among operational non-core components (armor weighted higher if present), falling through to Core when no outer components remain; explicit component targeting is supported via command parameter | Makes armor plating meaningfully protect subsystems and Core while supporting tactical targeting. |
| **Weight Tradeoffs** | No hard weight cap; total mass reduces move speed, increases displacement resistance, and increases vulnerability to heavy-target attacks (Titan, Gravity Crush) | Aligns with emergent-tradeoff design philosophy. |

---

## 3. Unresolved Design Questions

- **Ammunition replenishment between encounters:** For the vertical slice, returning to the Crest Hangar allows repairing/replacing components and refilling ammo freely; resource/manufacturing costs are deferred to Workshop systems.
- **Line of sight / cover:** Vertical slice uses an open 2D tactical arena with distance, range, and displacement; obstacle/cover geometry is deferred.

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
- Single test encounter with 3 enemy archetypes (Skirmisher, Bruiser, Artillery); repeatable with any Crest configuration.
- Functional vector/shape graphics in WPF rather than final art assets.
