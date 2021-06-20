#version 440
struct Material {
	vec4 ambient;
	vec4 diffuse;
	vec4 specular;
	float shininess;
};

struct Light {
	vec3 position;
	vec4 ambient;
	vec4 diffuse;
	vec4 specular;
};


layout (set = 0, binding = 1) uniform LightBuffer
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

layout(set = 0, binding = 5) uniform sampler2D texture0;

layout(location = 0) out vec4 outputColor;
layout(location = 0) in vec2 texCoord;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec3 FragPos;

void main()
{
	// ambient
	vec4 ambient = light.ambient * material.ambient;

	// diffuse
	vec3 norm = normalize(Normal);
	vec3 lightDir = normalize(light.position - FragPos);
	float diff = max(dot(norm, lightDir), 0.0);
	vec4 diffuse = light.diffuse * (diff * material.diffuse);

	// specular
	vec3 viewDir = normalize(viewPos - FragPos);
	vec3 reflectDir = reflect(-lightDir, norm);
	float spec = pow(max(dot(viewDir, reflectDir), 0.0), material.shininess);
	vec4 specular = light.specular * (spec * material.specular);

	vec4 result = ambient + diffuse + specular;
	outputColor = texture(texture0, texCoord) * result * color;
}