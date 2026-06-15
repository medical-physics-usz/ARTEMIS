from importlib.util import find_spec


def test_package_entrypoint_modules_are_discoverable():
    assert find_spec("artemis_preprocessing") is not None
    assert find_spec("artemis_preprocessing.cli") is not None
    assert find_spec("artemis_preprocessing.main") is not None
    assert find_spec("artemis_preprocessing.__main__") is not None
