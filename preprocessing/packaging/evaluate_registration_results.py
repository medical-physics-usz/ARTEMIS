import argparse
import os
from pathlib import Path

import matplotlib.pyplot as plt
import pandas as pd


def default_log_file() -> Path:
    return Path(os.environ.get("REGISTRATION_LOG_FILE", "registration_log.csv"))


def default_analysis_file(log_file: Path) -> Path:
    configured = os.environ.get("REGISTRATION_LOG_ANALYSIS_FILE")
    if configured:
        return Path(configured)
    return log_file.with_name(f"{log_file.stem}_analysis{log_file.suffix}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Evaluate ARTEMIS registration log results.")
    parser.add_argument("--log-file", type=Path, default=default_log_file())
    parser.add_argument("--analysis-file", type=Path)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    log_file = args.log_file
    analysis_file = args.analysis_file or default_analysis_file(log_file)

    df = pd.read_csv(log_file)

    # translations
    df["it_x"] = df["initial_transform"].str.split(",").str.get(0).astype(float)
    df["it_y"] = df["initial_transform"].str.split(",").str.get(1).astype(float)
    df["it_z"] = df["initial_transform"].str.split(",").str.get(2).astype(float)
    df["tt_x"] = df["fine_tuned_transform"].str.split(",").str.get(0).astype(float)
    df["tt_y"] = df["fine_tuned_transform"].str.split(",").str.get(1).astype(float)
    df["tt_z"] = df["fine_tuned_transform"].str.split(",").str.get(2).astype(float)
    df["ft_x"] = df["final_transform"].str.split(",").str.get(0).astype(float)
    df["ft_y"] = df["final_transform"].str.split(",").str.get(1).astype(float)
    df["ft_z"] = df["final_transform"].str.split(",").str.get(2).astype(float)

    # shifts
    df["tt-it_x"] = df["tt_x"] - df["it_x"]
    df["tt-it_y"] = df["tt_y"] - df["it_y"]
    df["tt-it_z"] = df["tt_z"] - df["it_z"]
    df["ft-it_x"] = df["ft_x"] - df["it_x"]
    df["ft-it_y"] = df["ft_y"] - df["it_y"]
    df["ft-it_z"] = df["ft_z"] - df["it_z"]

    df_s = df[df["accepted"] == True]
    df.to_csv(analysis_file, index=False)

    print("Shifts from initial to tuned")
    for col in ["x", "y", "z"]:
        print(f"{col}: {df_s[f'tt-it_{col}'].abs().max()}")

    print()
    print("Shifts from initial to final")
    for col in ["x", "y", "z"]:
        print(f"{col}: {df_s[f'ft-it_{col}'].abs().max()}")

    print("Normalized mutual information")
    print(df_s["normalized_mutual_information"].describe())
    plt.hist(df_s["normalized_mutual_information"])
    plt.show()


if __name__ == "__main__":
    main()
