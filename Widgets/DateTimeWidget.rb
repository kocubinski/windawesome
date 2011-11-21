
class DateTimeWidget
	include Windawesome::IFixedWidthWidget
	include System
	include System::Drawing
	include System::Windows::Forms
	include System::Linq

	def initialize string, back_color = nil, fore_color = nil, update_time = 30000, click = nil
		@background_color = back_color || Color.from_argb(0xC0, 0xC0, 0xC0)
		@foreground_color = fore_color || Color.from_argb(0x00, 0x00, 0x00)
		@string = string
		@click = click

		@update_timer = Timer.new
		@update_timer.interval = update_time
		@update_timer.tick do |s, ea|
			old_left = @label.left
			old_right = @label.right
			old_width = @label.width
			@label.text = " " + DateTime.now.to_string(@string) + " "
			@label.width = TextRenderer.measure_text(@label.text, @label.font).width
			if old_width != @label.width
				self.reposition_controls old_left, old_right
				@bar.do_fixed_width_widget_width_changed self
			end
		end
	end

	def static_initialize_widget windawesome; end

	def initialize_widget bar
		@bar = bar

		@label = bar.create_label " " + DateTime.now.to_string(@string) + " ", 0

		@label.text_align = ContentAlignment.middle_center
		@label.back_color = @background_color
		@label.fore_color = @foreground_color
		@label.click.add @click if @click

		@update_timer.start
	end

	def get_initial_controls is_left
		@is_left = is_left

		Enumerable.repeat @label, 1
	end

	def reposition_controls left, right
		if @is_left
			@label.location = Point.new left, 0
		else
			@label.location = Point.new right - @label.width, 0
		end
	end

	def get_left
		@label.left
	end

	def get_right
		@label.right
	end

	def static_dispose; end

	def dispose; end

	def refresh; end

end
