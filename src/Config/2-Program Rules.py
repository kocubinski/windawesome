from System.Linq import Enumerable
from Windawesome import ProgramRule, State, OnWindowShownAction
from Windawesome.NativeMethods import WS

config.ProgramRules = Enumerable.ToArray[ProgramRule]([
	ProgramRule(
		className = "^TApplication$",
		displayName = "^Find and Run Robot*",
		isManaged = False
	),
	ProgramRule(
		className = "^TApplication$",
		handleOwnedWindows = True
	),
	ProgramRule(
		className = "^Vim$",
		windowCreatedDelay = 200,
        onWindowCreatedAction = OnWindowShownAction.HideWindow,
		rules = [ProgramRule.Rule(workspace = 3, titlebar = State.HIDDEN, windowBorders = State.HIDDEN)]
	),
	ProgramRule(
		className = "^MozillaWindowClass$",
		rules = [ProgramRule.Rule(workspace = 2)]
	),
	ProgramRule(
		className = "^MozillaDialogClass$",
		rules = [ProgramRule.Rule(workspace = 2, isFloating = True)]
	),
	ProgramRule(
		className = "^CabinetWClass$", # Windows Explorer
		rules = [ProgramRule.Rule(workspace = 1)]
	),
	ProgramRule(
		displayName = ".*BitComet.*",
		windowCreatedDelay = 1000,
        onWindowCreatedAction = OnWindowShownAction.HideWindow,
		rules = [ProgramRule.Rule(workspace = 9, titlebar = State.HIDDEN, windowBorders = State.HIDDEN)]
	),
	ProgramRule(
		displayName = ".*SA Dictionary 2010.*",
		rules = [ProgramRule.Rule(isFloating = True)]
	),
	ProgramRule(
		className = "^rctrl_renwnd32$", # Outlook
		rules = [ProgramRule.Rule(workspace = 8)]
	),
	ProgramRule(
		className = "^TLoginForm.*", # Skype
		isManaged = False
	),
	ProgramRule(
		className = "^tSkMainForm.*", # Skype
		rules = [ProgramRule.Rule(workspace = 4, titlebar = State.HIDDEN, windowBorders = State.HIDDEN)]
	),
	ProgramRule(
		className = "^TConversationForm.*", # Skype
		rules = [ProgramRule.Rule(workspace = 4, titlebar = State.HIDDEN, windowBorders = State.HIDDEN)]
	),
	ProgramRule(
		className = "^__oxFrame.class__$", # ICQ
		displayName = "^ICQ$",
		rules = [ProgramRule.Rule(workspace = 4, isFloating = True)]
	),
	ProgramRule(
		className = "^__oxFrame.class__$", # ICQ, chat window
        onWindowCreatedAction = OnWindowShownAction.TemporarilyShowWindowOnCurrentWorkspace,
		rules = [ProgramRule.Rule(workspace = 4)]
	),
	ProgramRule(
		className = "^MediaPlayerClassicW$",
		rules = [ProgramRule.Rule(workspace = 1)]
	),
	ProgramRule(
		className = "^OpusApp$",
		displayName = ".*Microsoft Word Viewer$",
		windowCreatedDelay = 200
	),
	ProgramRule(
		className = "^mintty$",
		redrawDesktopOnWindowCreated = True,
		rules = [ProgramRule.Rule(workspace = 3, titlebar = State.HIDDEN, windowBorders = State.HIDDEN)]
	),
	ProgramRule(
		className = "^{97E27FAA-C0B3-4b8e-A693-ED7881E99FC1}$", # Foobar2000
		rules = [ProgramRule.Rule(workspace = 7)]
	),
	ProgramRule(
		displayName = ".*Microsoft Visual Studio.*",
		windowCreatedDelay = 2000,
        onWindowCreatedAction = OnWindowShownAction.HideWindow,
		rules = [ProgramRule.Rule(workspace = 5, titlebar = State.HIDDEN, windowBorders = State.HIDDEN)]
	),
	ProgramRule(
		className = "^XLMAIN$", # Excel
		rules = [ProgramRule.Rule(redrawOnShow = True)]
	),
	ProgramRule(
		className = "^QuickTimePlayerMain$",
		rules = [ProgramRule.Rule(workspace = 1, isFloating = True)]
	),
	ProgramRule(className = "^ConsoleWindowClass$", rules = [ProgramRule.Rule(isFloating = True)]), # Interix terminal
	ProgramRule(className = "^Internet Explorer_Hidden$", displayName = "", isManaged = False), # what the hell
	ProgramRule(className = "^Edit$", displayName = "", isManaged = False), # some stupid window of Adobe Reader
	ProgramRule(className = "^ShockwaveFlashFullScreen$", displayName = "^Adobe Flash Player$", isManaged = False), # Adobe Flash Player when full screen
	ProgramRule(displayName = "^Windows 7 Manager - Junk File Cleaner$", isManaged = False),
	ProgramRule(displayName = "^Windows 7 Manager - Registry Cleaner$", isManaged = False),
	ProgramRule(displayName = ".*Windows 7 Manager.*", rules = [ProgramRule.Rule(workspace = 1)]),
	ProgramRule(className = "^MsiDialogCloseClass$", isManaged = False),
	ProgramRule(className = "^MsiDialogNoCloseClass$", isManaged = False),
	ProgramRule(
		className = "^#32770$", # all dialogs
		rules = [ProgramRule.Rule(isFloating = True)] # should be floating
	),
	ProgramRule(
		styleContains = WS.WS_POPUP, # this captures class Shell_TrayWnd (the Windows Tasbar) and class Progman (the desktop window)
		isManaged = False
	),
	ProgramRule(
		styleNotContains = WS.WS_MAXIMIZEBOX,
		rules = [ProgramRule.Rule(isFloating = True)]
	),
	ProgramRule() # there SHOULD be a catch-all rule in the end!
])
