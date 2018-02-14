
#include <cuda.h> 
#include <device_launch_parameters.h> 
#include <texture_fetch_functions.h> 
#include <builtin_types.h> 
#include <vector_functions.h> 
#include <float.h>

#define _SIZE_T_DEFINED 
#ifndef __CUDACC__ 
#define __CUDACC__ 
#endif 
#ifndef __cplusplus 
#define __cplusplus 
#endif

typedef __int32 int32_t;
typedef unsigned __int32 uint32_t;
// Texture reference

extern "C"
{
#include <cuComplex.h>
#define MATRIX_SIZE  512
#define	TILE_SIZE 32
#define BLOCK_SIZE 32

	__global__ void MatrixMulKernel(cuComplex *A, cuComplex *B, cuComplex *C)
	{
		// inner product of A and B.conj()
		const int W = MATRIX_SIZE;

		// Block index
		const int bx = blockIdx.x;
		const int by = blockIdx.y;

		// Thread index
		const int tx = threadIdx.x;
		const int ty = threadIdx.y;

		// Index of the first sub-matrix 
		const int INDEX = W * BLOCK_SIZE * bx + BLOCK_SIZE *by + W*tx + ty;

		cuComplex conjB = make_cuComplex(cuCimagf(B[INDEX]), cuCrealf(B[INDEX]));
		cuComplex Csub = cuCmulf(A[INDEX], conjB);
		C[INDEX] = make_cuComplex(cuCrealf(Csub), cuCimagf(Csub));

	}

	__global__ void int2complex(cuFloatComplex *A, int32_t *B)
	{
		const int W = MATRIX_SIZE;

		// Block index
		const int bx = blockIdx.x;
		const int by = blockIdx.y;

		// Thread index
		const int tx = threadIdx.x;
		const int ty = threadIdx.y;

		// Index of the first sub-matrix 
		const int INDEX = W * BLOCK_SIZE * bx + BLOCK_SIZE *by + W*tx + ty;

		A[INDEX] = make_cuComplex(float(B[INDEX]), 0);

	}

	__global__ void float2complex(cuFloatComplex *A, float *B)
	{
		const int W = MATRIX_SIZE;

		// Block index
		const int bx = blockIdx.x;
		const int by = blockIdx.y;

		// Thread index
		const int tx = threadIdx.x;
		const int ty = threadIdx.y;

		// Index of the first sub-matrix 
		const int INDEX = W * BLOCK_SIZE * bx + BLOCK_SIZE *by + W*tx + ty;

		A[INDEX] = make_cuFloatComplex(B[INDEX], 0);

	}

	__global__ void abs_of_complex(cuComplex *A, float *B)
	{
		const int W = MATRIX_SIZE;

		// Block index
		const int bx = blockIdx.x;
		const int by = blockIdx.y;

		// Thread index
		const int tx = threadIdx.x;
		const int ty = threadIdx.y;

		// Index of the first sub-matrix 
		const int INDEX = W * BLOCK_SIZE * bx + BLOCK_SIZE*by + W*tx + ty;

		B[INDEX] = cuCabsf(A[INDEX]);

	}

	__global__ void sample_mean(short * matrix, int pixelsPerLine,
		int linesPerFrame, int samplesPerPixel, int flipEvenRows, int32_t* result)
	{
		int idx = threadIdx.x + blockDim.x*blockIdx.x;
		int result_idx = 0;
		int col = 0;
		int num_sample = 0;
		int this_value = 0;
		
		if (idx<pixelsPerLine*linesPerFrame*samplesPerPixel - samplesPerPixel+1){
			if ((idx - (idx / samplesPerPixel)*samplesPerPixel) == 0){
				result_idx = idx / samplesPerPixel;
				col = result_idx - (result_idx / pixelsPerLine)*pixelsPerLine;
				if ((result_idx / pixelsPerLine) - ((result_idx / pixelsPerLine) / 2) * 2 != flipEvenRows){
					result_idx = result_idx + pixelsPerLine - 2 * col - 1;
				}

				for (int i = 0; i < samplesPerPixel; i++){
					if (matrix[idx + i]>8192){
						this_value += matrix[idx + i] - 8192;
						num_sample += 1;
					}
				}

				if (num_sample>0){ result[result_idx] = this_value / num_sample; }
			}
		}
	}

	__global__ void sample_mean_debug(short * matrix, float* result)
	{
		int idx = threadIdx.x + blockDim.x*blockIdx.x;
		int result_idx = 0;
		int col = 0;
		int num_sample = 0;
		int this_value = 0;

		if (idx<512*512*3){
			if ((idx - (idx /3)*3) == 0){
				result_idx = idx / 3;
				col = result_idx - (result_idx / 512)*512;
				if ((result_idx / 512) - ((result_idx / 512) / 2) * 2 != 1){
					result_idx = result_idx + 512 - 2 * col - 1;
				}

				for (int i = 0; i < 3; i++){
					if (matrix[idx + i]>8192){
						this_value += matrix[idx + i] - 8192;
						num_sample += 1;
					}
				}
				result[result_idx] = 0;
				if (num_sample>0){ result[result_idx] = (float)this_value / (float)num_sample; }
				

			}
		}
	}
}