#version 440

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec3 aNormal;

layout(set = 0, binding = 0) uniform MVP
{
    mat4 Projection;
    mat4 View;
    mat4 Model;
};

layout(location = 0) out vec2 texCoord;
layout(location = 1) out vec3 Normal;
layout(location = 2) out vec3 FragPos;

void main(void)
{
	texCoord = aTexCoord;
	gl_Position = vec4(aPosition, 1.0) * Model * View * Projection;
	FragPos = vec3(vec4(aPosition, 1.0) * Model);
	Normal = aNormal * mat3(transpose(inverse(Model)));
}