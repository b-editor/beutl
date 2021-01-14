#version 330

out vec4 outputColor;

in vec2 texCoord;

uniform vec4 color;
uniform sampler2D texture;

void main()
{
    outputColor = texture(texture, texCoord) * color;
}