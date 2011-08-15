from System.Drawing import Font, Color
from System.Linq import Enumerable
from Windawesome import ILayout, TileLayout, FullScreenLayout, FloatingLayout, IPlugin, WindowSubclassing, Workspace
from Windawesome import Bar, LayoutWidget, WorkspacesWidget, ApplicationTabsWidget, SystemTrayWidget, CpuMonitorWidget
from Windawesome import LoggerPlugin, ShortcutsManager
from Windawesome.NativeMethods import MOD
from System import Tuple
from System.Windows.Forms import Keys

config.WindowBorderWidth = 1
config.WindowPaddedBorderWidth = 0

config.UniqueHotkey = Tuple[MOD, Keys](MOD.MOD_ALT, Keys.D0)

config.Bars = Enumerable.ToArray[Bar]([
	Bar(
		[WorkspacesWidget(), LayoutWidget()],
		[SystemTrayWidget(True), DateTimeWidget("ddd, d-MMM"), DateTimeWidget("h:mm tt", Color.FromArgb(0xA8, 0xA8, 0xA8))],
		[ApplicationTabsWidget()]
	),
	Bar(
		[WorkspacesWidget(), LayoutWidget()],
		[SystemTrayWidget(), DateTimeWidget("ddd, d-MMM"), DateTimeWidget("h:mm tt", Color.FromArgb(0xA8, 0xA8, 0xA8))],
		[ApplicationTabsWidget()]
	)
])

config.Workspaces = Enumerable.ToArray[Workspace]([
	Workspace(FloatingLayout(), [config.Bars[0]], name = 'main'),
	Workspace(FullScreenLayout(), [config.Bars[1]], name = 'web'),
	Workspace(FullScreenLayout(), [config.Bars[1]]),
	Workspace(TileLayout(), [config.Bars[1]], name = 'chat'),
	Workspace(FullScreenLayout(), [config.Bars[1]]),
	Workspace(FullScreenLayout(), [config.Bars[1]]),
	Workspace(FullScreenLayout(), [config.Bars[1]]),
	Workspace(FullScreenLayout(), [config.Bars[1]], name = 'mail'),
	Workspace(FullScreenLayout(), [config.Bars[1]], name = 'BC')
])
config.StartingWorkspace = 1 # workspace indices start from one!

config.Plugins = Enumerable.ToArray[IPlugin]([
	#WindowSubclassing([
	#	Tuple[str, str]("rctrl_renwnd32", ".*")
	#]),
	#LoggerPlugin(logWorkspaceSwitching = True, logWindowMinimization = True, logWindowRestoration = True,
	#	logActivation = True),
	ShortcutsManager()
])
