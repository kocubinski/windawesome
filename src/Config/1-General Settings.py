from System.Drawing import Font, Color
from System.Linq import Enumerable
from Windawesome import ILayout, TileLayout, FullScreenLayout, FloatingLayout, IPlugin, WindowSubclassing, Workspace
from Windawesome import Bar, LayoutWidget, WorkspacesWidget, ApplicationTabsWidget, SystemTrayWidget, CpuMonitorWidget
from Windawesome import LoggerPlugin, ShortcutsManager
from Windawesome.NativeMethods import MOD
from Windawesome.Workspace import BarInfo
from System import Tuple
from System.Windows.Forms import Keys

config.BorderWidth = 1
config.PaddedBorderWidth = 0

config.UniqueHotkey = Tuple[MOD, Keys](MOD.MOD_CONTROL | MOD.MOD_ALT, Keys.Add)

config.Layouts = Enumerable.ToArray[ILayout]([TileLayout(), FullScreenLayout(), FloatingLayout()])

config.Bars = Enumerable.ToArray[Bar]([
	Bar(
		[WorkspacesWidget(), LayoutWidget()],
		[SystemTrayWidget(), DateTimeWidget("ddd, d-MMM"), DateTimeWidget("h:mm tt", Color.FromArgb(0xA8, 0xA8, 0xA8))],
		[ApplicationTabsWidget()]
	)
])

config.Workspaces = Enumerable.ToArray[Workspace]([
	Workspace(config.Layouts[2], [BarInfo(config.Bars[0])], name = 'main'),
	Workspace(config.Layouts[1], [BarInfo(config.Bars[0])], name = 'web'),
	Workspace(config.Layouts[1], [BarInfo(config.Bars[0])]),
	Workspace(config.Layouts[0], [BarInfo(config.Bars[0])], name = 'chat'),
	Workspace(config.Layouts[1], [BarInfo(config.Bars[0])]),
	Workspace(config.Layouts[1], [BarInfo(config.Bars[0])]),
	Workspace(config.Layouts[1], [BarInfo(config.Bars[0])]),
	Workspace(config.Layouts[1], [BarInfo(config.Bars[0])], name = 'mail'),
	Workspace(config.Layouts[1], [BarInfo(config.Bars[0])], name = 'BC')
])
config.StartingWorkspace = 1 # workspace indices start from one!
config.WorkspacesCount = config.Workspaces.Length

config.Plugins = Enumerable.ToArray[IPlugin]([
	#WindowSubclassing([
	#	Tuple[str, str]("rctrl_renwnd32", ".*")
	#]),
	#LoggerPlugin(logWorkspaceSwitching = True, logWindowMinimization = True, logWindowRestoration = True,
	#	logActivation = True),
	ShortcutsManager()
])
