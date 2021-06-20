#version 440

layout(location = 0) in vec3 aPos;

layout(set = 0, binding = 0) uniform MVP
{
    mat4 Projection;
    mat4 View;
    mat4 Model;
};

void main()
{
	gl_Position = vec4(aPos.x, aPos.y, aPos.z, 1.0) * Model * View * Projection;
}