#version 440

layout(location = 0) in vec3 Position;

layout(set = 0, binding = 0) uniform ProjectionBuffer
{
    mat4 Projection;
};

layout(set = 0, binding = 1) uniform ViewBuffer
{
    mat4 View;
};

layout(set = 1, binding = 0) uniform WorldBuffer
{
    mat4 World;
};

void main()
{
	gl_Position = vec4(Position, 1.0) * World * View * Projection;
}