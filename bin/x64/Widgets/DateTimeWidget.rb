
class DateTimeWidget
	include Windawesome::IWidget
	include System
	include System::Drawing
	include System::Windows::Forms
	include System::Linq

	def initialize string, backColor = nil, foreColor = nil, updateTime = 30000, click = nil
		@backgroundColor = backColor || Color.from_argb(0xC0, 0xC0, 0xC0)
		@foregroundColor = foreColor || Color.from_argb(0x00, 0x00, 0x00)
		@string = string
		@click = click

		@updateTimer = Timer.new
		@updateTimer.interval = updateTime
		@updateTimer.tick.add lambda { |s, ea|
			oldWidth = @label.width
			@label.text = " " + DateTime.now.to_string(@string) + " "
			@label.width = TextRenderer.measure_text(@label.text, @label.font).width
			if oldWidth != @label.width
				self.reposition_controls @left, @right
				@bar.on_fixed_width_widget_width_changed self
			end
		}
	end

	def get_widget_type
		Windawesome::WidgetType.fixed_width
	end

	def static_initialize_widget windawesome, config; end

	def initialize_widget bar
		@bar = bar

		@label = bar.create_label " " + DateTime.now.to_string(@string) + " ", 0
		@label.text_align = ContentAlignment.middle_center
		@label.back_color = @backgroundColor
		@label.fore_color = @foregroundColor
		@label.click.add @click if @click

		@updateTimer.start
	end

	def get_controls left, right
		@isLeft = right == -1

		self.reposition_controls left, right

		res = Array[Control].new 1
		res[0] = @label
		res
	end

	def reposition_controls left, right
		@left = left
		@right = right

		if @isLeft
			@label.location = Point.new left, 0
			@right = @label.right
		else
			@label.location = Point.new right - @label.width, 0
			@left = @label.left
		end
	end

	def get_left
		@left
	end

	def get_right
		@right
	end

	def widget_shown; end

	def widget_hidden; end

	def static_dispose; end

	def dispose
		@updateTimer.stop
	end

end
