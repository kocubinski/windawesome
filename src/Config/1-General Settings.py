from System.Drawing import Font, Color
from System.Linq import Enumerable
from Windawesome import ILayout, TileLayout, FullScreenLayout, FloatingLayout, IPlugin, WindowSubclassing, Workspace
from Windawesome import Bar, LayoutWidget, WorkspacesWidget, ApplicationTabsWidget, SystemTrayWidget, CPUMonitorWidget
from Windawesome import LoggerPlugin, ShortcutsManager
from Windawesome.NativeMethods import MOD
from System import Tuple
from System.Windows.Forms import Keys

config.borderWidth = 1
config.paddedBorderWidth = 0

config.uniqueHotkey = Tuple[MOD, Keys](MOD.MOD_ALT | MOD.MOD_CONTROL | MOD.MOD_WIN | MOD.MOD_SHIFT, Keys.Add)

config.layouts = Enumerable.ToArray[ILayout]([TileLayout(), FullScreenLayout(), FloatingLayout()])

config.bars = Enumerable.ToArray[Bar]([
	Bar(
		[WorkspacesWidget(), LayoutWidget()],
		[SystemTrayWidget(True), DateTimeWidget("ddd, d-MMM-yy"), DateTimeWidget("h:mm tt", Color.FromArgb(0xa8, 0xa8, 0xa8))],
		[ApplicationTabsWidget(True)]
	),
	Bar(
		[WorkspacesWidget(), LayoutWidget()],
		[SystemTrayWidget(), DateTimeWidget("ddd, d-MMM-yy"), DateTimeWidget("h:mm tt", Color.FromArgb(0xa8, 0xa8, 0xa8))],
		[ApplicationTabsWidget()]
	)
])

config.workspaces = Enumerable.ToArray[Workspace]([
	Workspace(config.layouts[2], [config.bars[0]], name = 'main'),
	Workspace(config.layouts[1], [config.bars[1]], name = 'web'),
	Workspace(config.layouts[1], [config.bars[1]]),
	Workspace(config.layouts[0], [config.bars[1]], name = 'chat'),
	Workspace(config.layouts[1], [config.bars[1]]),
	Workspace(config.layouts[1], [config.bars[1]]),
	Workspace(config.layouts[1], [config.bars[1]]),
	Workspace(config.layouts[1], [config.bars[1]], name = 'mail'),
	Workspace(config.layouts[1], [config.bars[1]], name = 'BC')
])
config.startingWorkspace = 1 # workspace indices start from one!
config.workspacesCount = config.workspaces.Length

config.plugins = Enumerable.ToArray[IPlugin]([
	#WindowSubclassing([
	#	Tuple[str, str]("rctrl_renwnd32", ".*")
	#]),
	#LoggerPlugin(logWorkspaceSwitching = True, logWindowMinimization = True, logWindowRestoration = True,
	#	logActivation = True),
	ShortcutsManager()
])
