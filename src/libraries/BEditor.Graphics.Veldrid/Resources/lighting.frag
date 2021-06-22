#version 450
struct Material
{
	vec4 ambient;
	vec4 diffuse;
	vec4 specular;
	float shininess;
};

struct Light
{
	vec3 position;
	vec4 ambient;
	vec4 diffuse;
	vec4 specular;
};

layout(set = 0, binding = 1) uniform LightBuffer
{
	Light light;
};

layout(set = 0, binding = 2) uniform MaterialBuffer
{
	Material material;
};

layout(set = 0, binding = 3) uniform ViewPosBuffer
{
	vec3 viewPos;
};

layout(set = 0, binding = 4) uniform ColorBuffer
{
	vec4 color;
};

layout(location = 0) in vec3 Normal;
layout(location = 1) in vec3 FragPos;
layout(location = 0) out vec4 FragColor;

void main()
{
	vec4 ambient = light.ambient * material.ambient;

	vec3 norm = normalize(Normal);
	vec3 lightDir = normalize(light.position - FragPos);
	float diff = max(dot(norm, lightDir), 0.0);
	vec4 diffuse = light.diffuse * (diff * material.diffuse);

	vec3 viewDir = normalize(viewPos - FragPos);
	vec3 reflectDir = reflect(-lightDir, norm);
	float spec = pow(max(dot(viewDir, reflectDir), 0.0), material.shininess);
	vec4 specular = light.specular * (spec * material.specular);

	vec4 result = ambient + diffuse + specular;
	FragColor = result * color;
}