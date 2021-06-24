#version 450
layout(location = 0) out vec4 fsout_color;

layout(set = 1, binding = 1) uniform ColorBuffer
{
	vec4 Color;
};

void main()
{
	fsout_color = Color;
}