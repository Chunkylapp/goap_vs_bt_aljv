import os

import matplotlib.pyplot as plt
import pandas as pd
import seaborn as sns

# Configuration for Academic Style
plt.style.use("seaborn-v0_8-paper")
sns.set_context("paper", font_scale=1.5)
sns.set_palette("colorblind")

# File paths
CSV_PATH = "SimulationTelemetry.csv"
OUTPUT_DIR = "Graphs_V2"


def main():
    if not os.path.exists(CSV_PATH):
        print(f"Error: {CSV_PATH} not found. Please run the Unity simulation first.")
        return

    # Read data
    df = pd.read_csv(CSV_PATH)

    # Create output directory
    if not os.path.exists(OUTPUT_DIR):
        os.makedirs(OUTPUT_DIR)

    # 1. Score Distribution (Boxplot)
    plt.figure(figsize=(8, 6))
    score_df = pd.melt(
        df,
        value_vars=["BT_Score", "GOAP_Score"],
        var_name="Algorithm",
        value_name="Products Delivered",
    )
    score_df["Algorithm"] = score_df["Algorithm"].str.replace("_Score", "")
    sns.boxplot(x="Algorithm", y="Products Delivered", data=score_df)
    plt.title("Score Distribution across Rounds")
    plt.ylabel("Products Delivered")
    plt.tight_layout()
    plt.savefig(os.path.join(OUTPUT_DIR, "1_Score_Distribution.png"), dpi=300)
    plt.close()

    # 2. Fuel Efficiency (Score per 100 units of Fuel)
    df["BT_Efficiency"] = (df["BT_Score"] / df["BT_Fuel"].replace(0, 1)) * 100
    df["GOAP_Efficiency"] = (df["GOAP_Score"] / df["GOAP_Fuel"].replace(0, 1)) * 100
    plt.figure(figsize=(8, 6))
    eff_df = pd.melt(
        df,
        value_vars=["BT_Efficiency", "GOAP_Efficiency"],
        var_name="Algorithm",
        value_name="Efficiency (Deliveries per 100 Fuel)",
    )
    eff_df["Algorithm"] = eff_df["Algorithm"].str.replace("_Efficiency", "")
    sns.barplot(
        x="Algorithm",
        y="Efficiency (Deliveries per 100 Fuel)",
        data=eff_df,
        errorbar="ci",
    )
    plt.title("Average Fuel Efficiency")
    plt.ylabel("Deliveries per 100 Fuel")
    plt.tight_layout()
    plt.savefig(os.path.join(OUTPUT_DIR, "2_Fuel_Efficiency.png"), dpi=300)
    plt.close()

    # 3. Score vs Layout Complexity (Scatter + Trendline)
    plt.figure(figsize=(10, 6))
    sns.regplot(
        x="Avg_Delivery_Dist",
        y="BT_Score",
        data=df,
        label="Behavior Tree",
        scatter_kws={"alpha": 0.6},
    )
    sns.regplot(
        x="Avg_Delivery_Dist",
        y="GOAP_Score",
        data=df,
        label="GOAP",
        scatter_kws={"alpha": 0.6},
    )
    plt.title("Performance vs. Supply Chain Complexity")
    plt.xlabel("Average Delivery Distance (Proximity)")
    plt.ylabel("Products Delivered (Score)")
    plt.legend()
    plt.tight_layout()
    plt.savefig(os.path.join(OUTPUT_DIR, "3_Performance_vs_Complexity.png"), dpi=300)
    plt.close()

    # 4. Deaths vs Layout Complexity
    plt.figure(figsize=(10, 6))
    sns.regplot(
        x="Avg_Delivery_Dist",
        y="BT_Deaths",
        data=df,
        label="Behavior Tree",
        scatter_kws={"alpha": 0.6},
    )
    sns.regplot(
        x="Avg_Delivery_Dist",
        y="GOAP_Deaths",
        data=df,
        label="GOAP",
        scatter_kws={"alpha": 0.6},
    )
    plt.title("Truck Deaths vs. Supply Chain Complexity")
    plt.xlabel("Average Delivery Distance (Proximity)")
    plt.ylabel("Number of Fuel Depletions (Deaths)")
    plt.legend()
    plt.tight_layout()
    plt.savefig(os.path.join(OUTPUT_DIR, "4_Deaths_vs_Complexity.png"), dpi=300)
    plt.close()

    # 5. Idle Time & Aborts Comparison
    fig, axes = plt.subplots(1, 2, figsize=(12, 5))

    idle_df = pd.melt(
        df,
        value_vars=["BT_Idle_s", "GOAP_Idle_s"],
        var_name="Algorithm",
        value_name="Idle Time (s)",
    )
    idle_df["Algorithm"] = idle_df["Algorithm"].str.replace("_Idle_s", "")
    sns.barplot(
        x="Algorithm", y="Idle Time (s)", data=idle_df, errorbar="ci", ax=axes[0]
    )
    axes[0].set_title("Average Idle Time per Round")

    abort_df = pd.melt(
        df,
        value_vars=["BT_Aborts", "GOAP_Aborts"],
        var_name="Algorithm",
        value_name="Total Aborts",
    )
    abort_df["Algorithm"] = abort_df["Algorithm"].str.replace("_Aborts", "")
    sns.barplot(
        x="Algorithm", y="Total Aborts", data=abort_df, errorbar="ci", ax=axes[1]
    )
    axes[1].set_title("Average Plan Aborts per Round")

    plt.tight_layout()
    plt.savefig(os.path.join(OUTPUT_DIR, "5_Behavioral_Metrics.png"), dpi=300)
    plt.close()

    # 6. GOAP Planning Time
    plt.figure(figsize=(8, 5))
    sns.lineplot(x="Round", y="GOAP_AvgPlan_ms", data=df, marker="o")
    plt.title("GOAP Average Planning Time per Round")
    plt.xlabel("Round")
    plt.ylabel("Time (ms)")
    plt.ylim(bottom=0)
    plt.tight_layout()
    plt.savefig(os.path.join(OUTPUT_DIR, "6_GOAP_Planning_Time.png"), dpi=300)
    plt.close()

    print(f"Graphs successfully generated in the '{OUTPUT_DIR}' directory.")


if __name__ == "__main__":
    main()
