#version 440
layout(location = 0) out vec4 FragColor;

layout(set = 0, binding = 1) uniform ColorBuffer
{
	vec4 color;
};

void main()
{
	FragColor = color;
}