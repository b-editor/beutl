#version 330
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

uniform Light light;
uniform Material material;
uniform vec4 color;
uniform sampler2D texture0;
uniform vec3 viewPos;

out vec4 outputColor;

in vec2 texCoord;
in vec3 Normal;
in vec3 FragPos;

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