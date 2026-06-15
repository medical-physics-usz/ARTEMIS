import multiprocessing

from artemis_preprocessing.gui.app import main as run_pipeline


def main() -> None:
    multiprocessing.freeze_support()
    run_pipeline()


if __name__ == "__main__":
    main()
