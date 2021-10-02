﻿#version 330 core

// Input vertex data, different for all executions of this shader.
layout(location = 0) in vec3 vertexPosition_modelspace;
layout(location = 1) in vec3 vertexNormal;
layout(location = 2) in vec2 vertexUV;

layout(std140) uniform lights {
    vec4 color1;
    vec4 direction1;
    vec4 color2;
    vec4 direction2;
};

// Output data ; will be interpolated for each fragment.
out vec2 UV;
out vec4 DiffuseColor;

// Values that stay constant for the whole mesh.
uniform mat4 MVP;

void main(){

    // Output position of the vertex, in clip space : MVP * position
    gl_Position =  MVP * vec4(vertexPosition_modelspace,1);

    // UV of the vertex. No special space for this one.
    UV = vertexUV;

    DiffuseColor = vec4(0.0f,0.0f,0.0f,1.0f);
    DiffuseColor += vec4(abs(dot(direction1.xyz,vertexNormal)) * color1.xyz,1.0f);
    DiffuseColor += vec4(abs(dot(direction2.xyz,vertexNormal)) * color2.xyz,1.0f);
}