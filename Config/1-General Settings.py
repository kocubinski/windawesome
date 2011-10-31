from System.Drawing import Font, Color
from System.Linq import Enumerable
from Windawesome import ILayout, TileLayout, FullScreenLayout, FloatingLayout, IPlugin, Workspace
from Windawesome import Bar, LayoutWidget, WorkspacesWidget, ApplicationTabsWidget, SystemTrayWidget, CpuMonitorWidget, LaptopBatteryMonitorWidget, LanguageBarWidget
from Windawesome import LoggerPlugin, ShortcutsManager
from Windawesome.NativeMethods import MOD
from System import Tuple
from System.Windows.Forms import Keys

def onLayoutLabelClick():
	if windawesome.CurrentWorkspace.Layout.LayoutName() == "Full Screen":
		windawesome.CurrentWorkspace.ChangeLayout(FloatingLayout())
	elif windawesome.CurrentWorkspace.Layout.LayoutName() == "Floating":
		windawesome.CurrentWorkspace.ChangeLayout(TileLayout())
	else:
		windawesome.CurrentWorkspace.ChangeLayout(FullScreenLayout())

config.WindowBorderWidth = 1
config.WindowPaddedBorderWidth = 0

config.UniqueHotkey = Tuple[MOD, Keys](MOD.MOD_ALT, Keys.D0)

config.Bars = Enumerable.ToArray[Bar]([
	Bar(windawesome.monitors[0],
		[WorkspacesWidget(), LayoutWidget(onClick = onLayoutLabelClick)],
		[SystemTrayWidget(), LanguageBarWidget(Color.FromArgb(0x99, 0xB4, 0xD1)),
			DateTimeWidget("ddd, d-MMM"), DateTimeWidget("h:mm tt", Color.FromArgb(0xA8, 0xA8, 0xA8))],
		[ApplicationTabsWidget()]
	),
	Bar(windawesome.monitors[0],
		[WorkspacesWidget(), LayoutWidget(onClick = onLayoutLabelClick)],
		[SystemTrayWidget(True), LanguageBarWidget(Color.FromArgb(0x99, 0xB4, 0xD1)),
			DateTimeWidget("ddd, d-MMM"), DateTimeWidget("h:mm tt", Color.FromArgb(0xA8, 0xA8, 0xA8))],
		[ApplicationTabsWidget()]
	)
 ])

config.Workspaces = Enumerable.ToArray[Workspace]([
	Workspace(windawesome.monitors[0], FloatingLayout(), [config.Bars[1]], name = 'main'),
	Workspace(windawesome.monitors[0], FullScreenLayout(), [config.Bars[0]], name = 'web'),
	Workspace(windawesome.monitors[0], FullScreenLayout(), [config.Bars[0]]),
	Workspace(windawesome.monitors[0], TileLayout(), [config.Bars[0]], name = 'chat'),
	Workspace(windawesome.monitors[0], FullScreenLayout(), [config.Bars[0]]),
	Workspace(windawesome.monitors[0], FullScreenLayout(), [config.Bars[0]]),
	Workspace(windawesome.monitors[0], FullScreenLayout(), [config.Bars[0]]),
	Workspace(windawesome.monitors[0], FullScreenLayout(), [config.Bars[0]], name = 'mail'),
	Workspace(windawesome.monitors[0], FullScreenLayout(), [config.Bars[0]], name = 'BC')
])

config.StartingWorkspaces = [config.Workspaces[0]]

config.Plugins = [
	#LoggerPlugin(logWorkspaceSwitching = True, logWindowMinimization = True, logWindowRestoration = True,
	#	logActivation = True),
	ShortcutsManager()
]
