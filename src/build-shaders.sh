mkdir -p ./content/shaders/
./tools/linux/glslc ./shaders-src/blit.vert -o ./content/shaders/blit.vert.spv
./tools/linux/glslc ./shaders-src/blit.frag -o ./content/shaders/blit.frag.spv
./tools/linux/glslc ./shaders-src/fixedfunction.vert -o ./content/shaders/fixedfunction.vert.spv
./tools/linux/glslc ./shaders-src/fixedfunction_lit.vert -o ./content/shaders/fixedfunction_lit.vert.spv
./tools/linux/glslc ./shaders-src/fixedfunction.frag -o ./content/shaders/fixedfunction.frag.spv
./tools/linux/glslc ./shaders-src/imgui.vert -o ./content/shaders/imgui.vert.spv
./tools/linux/glslc ./shaders-src/imgui.frag -o ./content/shaders/imgui.frag.spv
./tools/linux/glslc -fshader-stage=compute ./shaders-src/vcop.glsl -o ./content/shaders/vcop.spv