#version 440

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 1) out vec2 texCoord;

layout(set = 0, binding = 0) uniform MVP
{
    mat4 Model;
    mat4 View;
    mat4 Projection;
};

void main(void)
{
	texCoord = aTexCoord;
	gl_Position = vec4(aPosition, 1.0) * Model * View * Projection;
}