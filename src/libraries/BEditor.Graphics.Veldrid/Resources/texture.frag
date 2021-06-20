#version 450

layout(location = 0) out vec4 outputColor;
layout(location = 1) in vec2 texCoord;

layout(set = 0, binding = 1) uniform texture2D  texture0;
layout(set = 0, binding = 2) uniform sampler Sampler;
layout(set = 0, binding = 3) uniform ColorBuffer
{
	vec4 color;
};

void main()
{
	outputColor = texture(sampler2D(texture0, Sampler), texCoord) * color;
}