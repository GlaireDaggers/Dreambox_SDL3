mkdir -p ./content/shaders/
./tools/linux/glslc ./shaders-src/blit.vert -o ./content/shaders/blit.vert.spv
./tools/linux/glslc ./shaders-src/blit.frag -o ./content/shaders/blit.frag.spv
./tools/linux/glslc ./shaders-src/vu.vert -o ./content/shaders/vu.vert.spv
./tools/linux/glslc ./shaders-src/fixedfunction.frag -o ./content/shaders/fixedfunction.frag.spv
./tools/linux/glslc ./shaders-src/imgui.vert -o ./content/shaders/imgui.vert.spv
./tools/linux/glslc ./shaders-src/imgui.frag -o ./content/shaders/imgui.frag.spv

./tools/linux/glslc -fshader-stage=compute ./shaders-src/convert_yuv.glsl -o ./content/shaders/convert_yuv.spv