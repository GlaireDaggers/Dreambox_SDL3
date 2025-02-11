mkdir -p ./content/shaders/
./tools/linux/glslc ./shaders-src/blit.vert -o ./content/shaders/blit.vert.spv
./tools/linux/glslc ./shaders-src/blit.frag -o ./content/shaders/blit.frag.spv
./tools/linux/glslc ./shaders-src/blit_tv.frag -o ./content/shaders/blit_tv.frag.spv
./tools/linux/glslc ./shaders-src/blit_interlace.frag -o ./content/shaders/blit_interlace.frag.spv
./tools/linux/glslc ./shaders-src/vu.vert -o ./content/shaders/vu.vert.spv
./tools/linux/glslc ./shaders-src/fixedfunction.frag -o ./content/shaders/fixedfunction.frag.spv
./tools/linux/glslc ./shaders-src/imgui.vert -o ./content/shaders/imgui.vert.spv
./tools/linux/glslc ./shaders-src/imgui.frag -o ./content/shaders/imgui.frag.spv

./tools/linux/glslc ./shaders-src/gen_phase.frag -o ./content/shaders/gen_phase.frag.spv
./tools/linux/glslc ./shaders-src/gen_signal.frag -o ./content/shaders/gen_signal.frag.spv
./tools/linux/glslc ./shaders-src/dec_signal_1.frag -o ./content/shaders/dec_signal_1.frag.spv
./tools/linux/glslc ./shaders-src/dec_signal_2.frag -o ./content/shaders/dec_signal_2.frag.spv
./tools/linux/glslc ./shaders-src/sharpen.frag -o ./content/shaders/sharpen.frag.spv

./tools/linux/glslc -fshader-stage=compute ./shaders-src/convert_yuv.glsl -o ./content/shaders/convert_yuv.spv
./tools/linux/glslc -fshader-stage=compute ./shaders-src/depth_query.glsl -o ./content/shaders/depth_query.spv