#version 450

layout(location = 0) in vec2 fsin_texCoords;
layout(location = 0) out vec4 fsout_color;

layout(set = 1, binding = 1) uniform texture2D SurfaceTexture;
layout(set = 1, binding = 2) uniform sampler SurfaceSampler;
layout(set = 1, binding = 3) uniform ColorBufer
{
    vec4 Color;
};

void main()
{
    fsout_color = texture(sampler2D(SurfaceTexture, SurfaceSampler), fsin_texCoords) * Color;
    fsout_color.rgb /= fsout_color.a;
}