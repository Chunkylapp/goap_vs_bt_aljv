# Project Handout: Algorithmic Comparison of Behavior Trees vs. GOAP in a Logistics Simulation

## 1. Project Overview
This project serves as an empirical comparison between two prominent Artificial Intelligence architectures used in video games and robotics: **Behavior Trees (BT)** and **Goal-Oriented Action Planning (GOAP)**. 

The simulation environment is a mirrored, competitive logistics network. Trucks must haul resources across a dynamically generated NavMesh layout through a three-stage supply chain:
`Producers (Raw) -> Factories (Raw to Product) -> Consumers (Demand)`

Trucks consume fuel dynamically and must navigate to Gas Stations to prevent systemic deaths. The simulation automates 100-second rounds, scrambles the layout, records deep telemetry, and resets, providing a robust dataset for statistical analysis.

---

## 2. Algorithm 1: Behavior Tree (Reactive AI)
**Implementation File:** `SmartTruckAI.cs`

### 2.1 Core Architecture & Control Flow
The Behavior Tree is implemented as a hard-coded, cascading priority selector within the `Update()` loop. It evaluates the world frame-by-frame and greedily selects the best immediate action. The control flow mimics a classic Selector node in a BT:

1.  **Survival Node (Highest Priority):** If `myFuel < 30f`, override all tasks. Set target to the best Gas Station.
2.  **Delivery Node (Cargo == "Product"):** If holding a finished product, find the optimal Consumer and drop it off.
3.  **Transit Node (Cargo == "Raw"):** If holding raw materials, find the optimal Factory and drop it off.
4.  **Procurement Node (Cargo == "None"):** If empty, evaluate all Producers and all Factories. Compare their scores and go to the highest bidder to pick up cargo.

### 2.2 Mathematical Utility Scoring (`GetBestTarget`)
The BT does not pick the closest node; it calculates a dynamically weighted utility score for every valid building on the map.
*   **Base Score:** `Score = -Distance` (Favors closer targets).
*   **Utility Bonus:** `Score += (Utility * 15f)` (Heavily weights buildings that have full stock or high demand).
*   **Product Multiplier (The Bottleneck Fix):** To prevent the AI from endlessly shuttling raw materials while ignoring finished products, the evaluation mathematically forces a priority shift:
    ```csharp
    int factoryScore = GetNodeScore(bestFactory, false) * 2; // 2x multiplier for finished goods
    int producerScore = GetNodeScore(bestProducer, false);
    ```

### 2.3 Target Commitment (Anti-Thrashing / Hysteresis)
A common flaw in reactive BTs is "Path Thrashing." If Factory A and Factory B have identical scores, the AI might switch targets every single frame, causing the `NavMeshAgent` to freeze as it continuously recalculates paths. 
**The Solution:** We implemented *Hysteresis*. The AI locks onto `myCurrentTarget`. The `GetBestTarget()` function is *only* called if `myCurrentTarget == null` or if the `IsTargetValid()` check fails (e.g., another truck took the cargo first). Upon successful `TryPickUp` or `TryDropOff`, the target is explicitly set to `null`, forcing a fresh evaluation cycle.

---

## 3. Algorithm 2: GOAP (Goal-Oriented Action Planning)
**Implementation File:** `GOAPTruckAI.cs`

### 3.1 Core Architecture & State Representation
Unlike the BT's hardcoded `if/else` tree, the GOAP agent dynamically builds its logic at runtime. It relies on three core concepts:
*   **World State:** A `Dictionary<string, bool>` tracking `has_raw`, `has_product`, `is_empty`, and `task_complete`.
*   **Actions:** `TruckAction` objects representing discrete behaviors (e.g., "Pick Up Raw", "Refuel"). Each action has *Preconditions* (must be empty) and *Effects* (is_empty = false, has_raw = true).
*   **The Planner:** A recursive Depth-First Search (DFS) algorithm that strings Actions together until the simulated World State matches the Goal State (`task_complete == true`).

### 3.2 Asynchronous Multithreading (`StartAsyncPlan`)
Generating permutations of a graph with 20+ buildings across 4 depth layers requires thousands of calculations. To prevent framerate drops:
1.  The agent takes a thread-safe `NodeSnapshot` (capturing positions, IDs, and Utility scores) of the map.
2.  It takes a snapshot of the Global Claims Dictionary (`pickupsSnapshot`).
3.  It spins up a background thread using `await Task.Run(() => DoPlanSearch(...))`.
4.  The main Unity thread continues rendering smoothly while the AI explores the matrix. Telemetry proves this takes an average of only **2 to 4 milliseconds**.

### 3.3 The DFS Search Algorithm (`Search`)
The core of the "AI Brain" is a recursive function. It takes a virtual State, Fuel level, Position, and Current Score.
*   **Depth Limit:** Capped at `path.Count > 4`. This allows complex logistical chains like `Refuel -> Pick Up Raw -> Drop Off Raw -> Pick Up Product` in a single thought process.
*   **Fuel Prediction:** It simulates fuel drain during the search: `float fuelNeeded = dist * fuelDrainRate;`. If a simulated path causes `fuel < fuelNeeded`, that branch of the tree is instantly discarded (pruned).

### 3.4 Advanced Heuristics & Modifications
To outperform the BT, we injected specialized heuristics into the search space:

**A. Decentralized Swarm Logic (Cost-Sharing)**
GOAP agents are inherently selfish, which normally causes "Convoys" (5 trucks taking the exact same path to the same factory). We solved this via emergent swarm behavior.
During the search, the AI checks the `pickupsSnapshot`. If another truck has already reserved a factory, the AI artificially inflates the virtual distance to that factory:
```csharp
dist += (claimedPick + claimedDrop) * 40f; // Add 40m of virtual distance per claim
```
Because the DFS algorithm penalizes high distances, trucks organically "repel" each other and distribute evenly across the map without needing a centralized manager.

**B. Opportunistic Refueling**
The BT only refuels when it panics (<30% fuel). The GOAP evaluates refueling *proactively*. During the DFS search, if an action is "Refuel" and fuel is below 70%, it applies a dynamic bonus:
```csharp
actionScore += (70f - fuel) * 2f; 
```
If a truck is driving past a gas station at 40% fuel, the mathematical bonus of topping off outweighs the cost of the detour. This nearly eliminates fuel-depletion deaths.

**C. Partial Plan Acceptance**
If a factory is busy, a "perfect" plan (ending in `task_complete`) might be impossible to calculate. Instead of freezing, the DFS accepts partial plans (e.g., just driving to a factory to wait), but applies a massive `+1000f` bonus to plans that *do* achieve `task_complete`, ensuring trucks always prioritize finishing the supply chain.

---

## 4. Automation & Environment Telemetry
**Implementation File:** `CompetitionManager.cs`

### 4.1 Automated Shuffling & NavMesh Baking
To ensure valid data, the environment resets itself every 100 seconds. 
*   It records the exact local coordinates of user-placed buildings as "safe zones".
*   Upon reset, it shuffles these safe coordinates to randomize the layout without ever placing a building inside a wall or off the NavMesh.
*   It applies the shuffled layout to the BT root, and identically mirrors it to the GOAP root.
*   It triggers `surfaceBT.BuildNavMesh()` to recalculate the pathing grid at runtime.
*   It revives dead trucks, resets fuel, and uses `NavMeshAgent.Warp()` to snap them back to their starting positions.

### 4.2 Layout Complexity Index (`CalculateLayoutComplexity`)
Not all random maps are equal. A map with clumped buildings is easier than a sprawling map. The manager calculates an `Avg_Delivery_Dist` metric by measuring the physical world-space distance from every Producer to every Factory, and every Factory to every Consumer. This allows us to statistically correlate map scale against AI performance.

---

## 5. Telemetry & Python Data Analysis Pipeline
**Implementation File:** `generate_graphs.py`

At the end of every round, the manager appends 13 datapoints to `SimulationTelemetry.csv`, including:
*   `BT_Score` & `GOAP_Score`
*   `BT_Fuel` & `GOAP_Fuel`
*   `BT_Aborts` & `GOAP_Aborts` (Measuring decisive execution vs. plan failure)
*   `BT_Idle_s` & `GOAP_Idle_s` (Measuring supply chain starvation)
*   `BT_Deaths` & `GOAP_Deaths` (Measuring pathing safety)
*   `Avg_Delivery_Dist`

A custom Python script utilizing `pandas` and `seaborn` processes this CSV into 6 academic-grade graphs. These graphs empirically demonstrate:
1.  **Fuel Efficiency:** GOAP's multi-step planning results in significantly higher product deliveries per unit of fuel burned.
2.  **Scalability:** The scatter plots prove how GOAP sustains its score better than BT as the `Avg_Delivery_Dist` (map complexity) increases.
3.  **Behavioral Superiority:** The bar charts highlight that GOAP suffers fewer plan aborts and idle seconds, proving the efficacy of the Full-Plan Reservation and Swarm mechanics.

---

## 6. Iterative Improvements: V1 vs. V2 Analysis

During development, the telemetry pipeline revealed critical flaws in the initial implementations of both algorithms (V1). By applying mathematical heuristics (V2), we significantly improved performance and stability.

### 6.1 Changes from V1 to V2
* **BT Update (Target Commitment):** In V1, BT trucks evaluated targets every frame, leading to "path thrashing" and stuttering. In V2, we added hysteresis, forcing trucks to commit to a target until completion or invalidation.
* **GOAP Update (Swarm Logic):** V1 GOAP agents acted selfishly, causing "convoys" where multiple trucks queued for the same factory. V2 introduced a decentralized cost-sharing mechanic, adding virtual distance penalties to claimed nodes to naturally distribute the fleet.
* **GOAP Update (Opportunistic Refueling):** V1 GOAP only refueled when fuel dropped below 30% (Panic State), leading to high mortality on large maps. V2 added a heuristic bonus to refueling if a station is on the route and fuel is <70%, encouraging preventative top-offs.

### 6.2 Behavioral Differences and Trade-offs
* **BT Behavior:** The V2 BT drives much more decisively. By eliminating the computational stutter, the trucks physically move faster across the map, resulting in an increase in average score (from ~4.5 to ~5.5). However, its mortality rate remains high because it still relies on a reactive panic state for fuel.
* **GOAP Behavior:** The V2 GOAP is highly resilient. Opportunistic refueling caused its death rate to plummet by nearly 50% compared to V1. However, taking preventative detours for fuel costs physical time. This represents a classic **Risk vs. Reward Trade-off**: V2 GOAP sacrifices a tiny fraction of its peak theoretical throughput to guarantee long-term survival and consistency. The Swarm Logic completely eliminated the high variance in scoring, resulting in a perfectly stable supply chain.

### 6.3 Overall Conclusion
Based on the empirical data gathered across 25+ randomized rounds:
* **Throughput:** GOAP consistently delivers over double the products of the BT (averaging ~11.6 vs ~5.5).
* **Efficiency:** GOAP achieves more than double the deliveries per 100 units of fuel burned.
* **Scalability:** As the layout complexity (`Avg_Delivery_Dist`) increases, the BT collapses due to poor fuel management and reactive pathing. GOAP sustains its supply chain regardless of map size.

**Final Verdict:** **GOAP is unequivocally the superior algorithm** for complex logistical simulations. Its ability to look ahead, combined with the V2 emergent swarm and opportunistic heuristics, creates a robust, highly efficient, and self-sustaining AI network.

---
*Generated for the ALJV Final Presentation.*
