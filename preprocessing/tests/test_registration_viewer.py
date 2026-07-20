import gc
import weakref

from artemis_preprocessing.registration import core


class _FakeCanvas:
    def __init__(self):
        self.callbacks = {}

    def mpl_connect(self, event_name, callback):
        self.callbacks[event_name] = callback
        return len(self.callbacks)


class _FakeFigure:
    def __init__(self):
        self.canvas = _FakeCanvas()


class _FakeViewer:
    def __init__(self):
        self.fig = _FakeFigure()

    def show(self, block=True):
        self.show_block = block


def test_nonblocking_viewer_is_retained_until_figure_closes():
    viewer = _FakeViewer()
    viewer_ref = weakref.ref(viewer)
    figure = viewer.fig

    core._retain_nonblocking_viewer(viewer)
    del viewer
    gc.collect()

    assert viewer_ref() is not None

    figure.canvas.callbacks["close_event"](None)
    gc.collect()

    assert viewer_ref() is None


def test_run_viewer_retains_and_returns_nonblocking_viewer(monkeypatch):
    class _FakeImage:
        def GetPixelIDValue(self):
            return 1

        def GetSpacing(self):
            return (1.0, 1.0, 1.0)

    viewer = _FakeViewer()
    retained = []

    monkeypatch.setattr(core.sitk, "Resample", lambda *args, **kwargs: args[0])
    monkeypatch.setattr(core.sitk, "GetArrayFromImage", lambda image: image)
    monkeypatch.setattr(core, "MultiViewOverlay", lambda *args, **kwargs: viewer)
    monkeypatch.setattr(core, "_retain_nonblocking_viewer", retained.append)

    result = core.run_viewer(
        _FakeImage(),
        _FakeImage(),
        transform=object(),
        block=False,
    )

    assert result is viewer
    assert retained == [viewer]
    assert viewer.show_block is False
