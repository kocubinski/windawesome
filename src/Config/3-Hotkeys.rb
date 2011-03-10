
def subscribe(modifiers, key)
	Windawesome::ShortcutsManager.subscribe modifiers, key, lambda {
		ret = yield
		return true if ret == nil
		ret
	}
end

modifiers = Windawesome::ShortcutsManager::KeyModifiers
key = System::Windows::Forms::Keys

# quit Windawesome
subscribe modifiers.Alt | modifiers.Shift, key.Q do
	windawesome.quit
end

# refresh Windawesome
subscribe modifiers.Alt | modifiers.Shift, key.R do
	windawesome.refresh_windawesome
end

# quit application
subscribe modifiers.Alt, key.Q do
	windawesome.quit_application Windawesome::NativeMethods.get_foreground_window
end

# minimize application
subscribe modifiers.Alt, key.A do
	windawesome.minimize_application Windawesome::NativeMethods.get_foreground_window
end

# switch to previous workspace
subscribe modifiers.Alt, key.Oemtilde do
	windawesome.switch_to_workspace Windawesome::Windawesome.previousWorkspace
end

# start Firefox
subscribe modifiers.Alt, key.F do
	windawesome.RunOrShowApplication "MozillaUIWindowClass", "C:\\Program Files (x86)\\Mozilla Firefox\\firefox.exe"
end

# start Explorer
subscribe modifiers.Alt, key.E do
	windawesome.run_application "C:\\Users\\Boris\\Desktop\\Downloads.lnk"
end

# start Hostile Takeover
subscribe modifiers.Alt, key.H do
	windawesome.run_application "C:\\Users\\Boris\\Downloads\\Hostile Takeover.txt"
end

# start Foobar2000
subscribe modifiers.Alt, key.W do
	windawesome.run_application "C:\\Program Files (x86)\\foobar2000\\foobar2000.exe"
end

# start Cygwin's MinTTY shell
subscribe modifiers.Alt, key.Return do
	className = Windawesome::NativeMethods.get_window_class_name Windawesome::NativeMethods.get_foreground_window
	if className != "MediaPlayerClassicW"
		windawesome.run_application "C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\Cygwin\\mintty.lnk"
	else
		false
	end
end

# start Bitcomet
subscribe modifiers.Alt, key.B do
	windawesome.run_application "C:\\Program Files (x86)\\BitComet\\BitComet.exe"
end

# toggle window floating
subscribe modifiers.Control | modifiers.Alt | modifiers.Shift, key.F do
	windawesome.toggle_window_floating Windawesome::NativeMethods.get_foreground_window
end

# toggle window titlebar
subscribe modifiers.Alt | modifiers.Shift, key.D do
	windawesome.toggle_show_hide_window_titlebar Windawesome::NativeMethods.get_foreground_window
end

# toggle windows taskbar
subscribe modifiers.Alt | modifiers.Control, key.Space do
	windawesome.toggle_taskbar_visibility
end

# toggle window border
subscribe modifiers.Alt | modifiers.Shift, key.B do
	windawesome.toggle_show_hide_window_border Windawesome::NativeMethods.get_foreground_window
end

# change layout to Tile
subscribe modifiers.Alt | modifiers.Shift, key.T do
	windawesome.current_workspace.change_layout config.layouts[0]
end

# change layout to Full Screen
subscribe modifiers.Alt | modifiers.Shift, key.M do
	windawesome.current_workspace.change_layout config.layouts[1]
end

# change layout to Floating
subscribe modifiers.Alt | modifiers.Shift, key.F do
	windawesome.current_workspace.change_layout config.layouts[2]
end

subscribe modifiers.Control | modifiers.Alt | modifiers.Shift, key.Q do
	windawesome.remove_application_from_current_workspace Windawesome::NativeMethods.get_foreground_window
end

subscribe modifiers.Alt | modifiers.Shift, key.L do
	windawesome.current_workspace.layout.toggle_layout_axis if windawesome.current_workspace.layout.layout_name == "Tile"
end

subscribe modifiers.Alt | modifiers.Shift, key.S do
	windawesome.current_workspace.layout.toggle_stack_area_axis if windawesome.current_workspace.layout.layout_name == "Tile"
end

subscribe modifiers.Control | modifiers.Alt | modifiers.Shift, key.S do
	windawesome.current_workspace.layout.toggle_master_area_axis if windawesome.current_workspace.layout.layout_name == "Tile"
end

subscribe modifiers.Alt | modifiers.Shift, key.Down do
	if windawesome.current_workspace.layout.layout_name == "Tile"
		window = windawesome.current_workspace.get_window Windawesome::NativeMethods.get_foreground_window
		windawesome.current_workspace.layout.shift_window_to_next_position window
	end
end

subscribe modifiers.Alt | modifiers.Shift, key.Up do
	if windawesome.current_workspace.layout.layout_name == "Tile"
		window = windawesome.current_workspace.get_window Windawesome::NativeMethods.get_foreground_window
		windawesome.current_workspace.layout.shift_window_to_previous_position window
	end
end

subscribe modifiers.Alt | modifiers.Shift, key.Left do
	windawesome.current_workspace.layout.add_to_master_area_factor -0.05 if windawesome.current_workspace.layout.layout_name == "Tile"
end

subscribe modifiers.Alt | modifiers.Shift, key.Right do
	windawesome.current_workspace.layout.add_to_master_area_factor if windawesome.current_workspace.layout.layout_name == "Tile"
end

subscribe modifiers.Alt | modifiers.Shift, key.Return do
	if windawesome.current_workspace.layout.layout_name == "Tile"
		window = windawesome.current_workspace.get_window Windawesome::NativeMethods.get_foreground_window
		windawesome.current_workspace.layout.shift_window_to_main_position window
	end
end

(1 .. config.workspacesCount).each do |i|
	k = eval("key.D" + i.to_s)

	subscribe modifiers.Alt, k do
		windawesome.switch_to_workspace i
	end

	subscribe modifiers.Alt | modifiers.Shift, k do
		windawesome.change_application_to_workspace Windawesome::NativeMethods.get_foreground_window, i
	end

	subscribe modifiers.Control | modifiers.Alt | modifiers.Shift, k do
		windawesome.add_application_to_workspace Windawesome::NativeMethods.get_foreground_window, i
	end
end
