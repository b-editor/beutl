#version 440

layout(location = 0) out vec4 fsout_color;

layout(set = 0, binding = 2) uniform ColorBuffer
{
    vec4 Color;
};

void main()
{
    fsout_color = Color;
}