#version 450
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 0) out vec3 Normal;
layout(location = 1) out vec3 FragPos;

layout(set = 0, binding = 0) uniform MVP
{
    mat4 Projection;
    mat4 View;
    mat4 Model;
};

void main()
{
	gl_Position = vec4(aPos, 1.0) * Model * View * Projection;
	FragPos = vec3(vec4(aPos, 1.0) * Model);
	Normal = aNormal * mat3(transpose(inverse(Model)));
}