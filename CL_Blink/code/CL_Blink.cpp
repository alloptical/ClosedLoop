/*********************************************************************
* Instructions for Running:
*  1. Setup directory to where your phase masks are saved.
*  2. Setup TCP server address 
*  3. Select the physical channel to correspond to where your signal is input on the DAQ device (optional)
*  4. Enter total number of phase masks
*  5. Enter 'Y' when you are ready to start experiment
*
* Notes
*   make sure no other application is controlling the SLM.
*   ensure that Blink_SDK.dll is in the same directory as the .exe file.



*********************************************************************/
#include <stdio.h>
#include "NIDAQmx.h"
#include <iostream>
#include <math.h> 
# include <fstream>
#include "stdafx.h"  // Does nothing but #include "targetver.h"

#include <vector>
#include <cstdio>
#include <conio.h>
#include "Blink_SDK.H"  // Relative path to SDK header

#include <chrono> // measure time

#include<io.h>      // TCP 
#include<stdio.h>
#include<winsock2.h>
#pragma comment(lib,"ws2_32.lib") //Winsock Library
#define DEFAULT_BUFLEN 2

using namespace std;
using namespace std::chrono;
#define DAQmxErrChk(functionCall) if( DAQmxFailed(error=(functionCall)) ) goto Error; else


bool bmp_08_data_read(std::ifstream &file_in, unsigned long int width, long int height,
	unsigned char *rarray);



// call back functions    for daq input only. can delete
int32 CVICALLBACK DoneCallback(TaskHandle taskHandle, int32 status, void *callbackData);
int32 CVICALLBACK SignalEventCallback(TaskHandle taskHandle, int32 signalID, void *callbackData);
int main(void)
{
	/*DAQ config*/
	int32       error = 0;
	TaskHandle  WriteTaskHandle = 0;
	int32   	written;
	float64     WriteData[10];
	int         totalWrite = 0;
	float64     lastdata = 0;
	char        errBuff[2048] = { '\0' };
	char        start = 'N';
	char        filename[32]; // bmp filename 
	int32       fileInx = 0;
	int32       LastFileInx = 0;

	for (int i = 0; i < 5; i++) // generate ao array to PV
		WriteData[i] = 5;
	for (int i = 5; i < 10; i++) // generate ao array to PV
		WriteData[i] = 0;

	/*TCP config*/

	WSADATA wsa;
	SOCKET s, new_socket;
	
	struct sockaddr_in server, client;
	int c;
	char *message;
	int iResult;
	int iSendResult;
	/*char recvbuf[DEFAULT_BUFLEN];*/
	int recvbuflen = DEFAULT_BUFLEN;

	// Read BMP file
	long int          height = 512;
	unsigned long int width = 512;
	int               numbytes;
	numbytes = width * abs(height) * sizeof(unsigned char);
	bool       errorBmp = false;
	int32      numMasks = 31;
	int32      numBytes = numbytes;
	unsigned char     **bmpArray = NULL;

	typedef std::vector<unsigned char>  uchar_vec;

	/* CLOCK */
	high_resolution_clock::time_point t1;
	high_resolution_clock::time_point t2;
	high_resolution_clock::time_point tSTART;
	high_resolution_clock::time_point tSTOP;

	/*********************************************/
	// Blink Configure Code
	/*********************************************/
	const int board_number = 1;
	const unsigned int bits_per_pixel = 8U;
	const unsigned int pixel_dimension = 512U;
	const bool         is_nematic_type = true;
	const bool         RAM_write_enable = true;
	const bool         use_GPU_if_available = true;
	const char* const  regional_lut_file = "C:/Program Files/Meadowlark Optics/Overdrive Plus/SLM_3331_encrypt.txt"; // slm lut dir; change the dir here

	unsigned int n_boards_found = 0U;
	bool         constructed_okay = true;
	/*Uncomment from here*/
	Blink_SDK sdk(bits_per_pixel, pixel_dimension, &n_boards_found,
		&constructed_okay, is_nematic_type, RAM_write_enable,
		use_GPU_if_available, 20U, regional_lut_file);

	// Check that everything started up successfully.
	bool okay = constructed_okay && sdk.Is_slm_transient_constructed();

	if (okay)
	{
		enum { e_n_true_frames = 5 };
		sdk.Set_true_frames(e_n_true_frames);
		sdk.SLM_power(true);
		okay = sdk.Load_linear_LUT(board_number);
	}
	/*Uncomment end*/

	/*********************************************/
	// Read bmp to array Code
	/*********************************************/

	printf("Number of phase masks:");
	cin >> numMasks;
	bmpArray = new unsigned char*[numMasks];
	for (int i = 0; i < numMasks; i++)
	{
		sprintf(filename, "C:/Zoe/ROIfiles/PhaseMasks/Pattern%i.bmp", i + 1);// change the dir here
		// load bmp file 
		ifstream   file_in;
		file_in.open(filename, ios::in | ios::binary);
		if (file_in)
		{
			bmpArray[i] = new unsigned char[numbytes];
			errorBmp = bmp_08_data_read(file_in, width, height, bmpArray[i]);
			if (errorBmp)
			{
				std::cout << "\n";
				std::cout << "BMP_READ_TEST - Fatal error!\n";
				std::cout << "  The test failed.\n";
			}
			// end load file
		}
		else{
			//error = true;
			cout << "\n";
			cout << "BMP_READ - Fatal error!\n";
			cout << "  Could not open the input file.\n";
			//return error;
		}


	}

	/*********************************************/
	// DAQmx Configure Code
	/*********************************************/

	// writing task   --WriteTaskHandle
	DAQmxErrChk(DAQmxCreateTask("", &WriteTaskHandle));
	DAQmxErrChk(DAQmxCreateAOVoltageChan(WriteTaskHandle, "Dev2/ao6", "", 0, 10.0, DAQmx_Val_Volts, NULL));       // ao pulses


	while (start != 'Y'){
		std::cout << "Start recording?";
		std::cin >> start;
	}

	/*********************************************/
	// TCP server Config code
	/*********************************************/

	printf("\nInitialising Winsock...");
	if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0)
	{
		printf("Failed. Error Code : %d", WSAGetLastError());
		return 1;
	}

	printf("Initialised.\n");

	//Create a socket
	if ((s = socket(AF_INET, SOCK_STREAM, 0)) == INVALID_SOCKET)
	{
		printf("Could not create socket : %d", WSAGetLastError());
	}

	printf("Socket created.\n");

	//Prepare the sockaddr_in structure
	server.sin_family = AF_INET;
	server.sin_addr.s_addr = INADDR_ANY;
	server.sin_port = htons(8888);

	//Bind
	if (bind(s, (struct sockaddr *)&server, sizeof(server)) == SOCKET_ERROR)
	{
		printf("Bind failed with error code : %d", WSAGetLastError());
	}

	puts("Bind done");

	//Listen to incoming connections
	listen(s, 3);

	//Accept and incoming connection
	puts("Waiting for incoming connections...");

	c = sizeof(struct sockaddr_in);
	new_socket = accept(s, (struct sockaddr *)&client, &c);
	if (new_socket == INVALID_SOCKET)
	{
		printf("accept failed with error code : %d", WSAGetLastError());
	}

	puts("Connection accepted");


	/*********************************************/
	// Main loop
	/*********************************************/
	 char *recvbuf = 0;
	do {
		recvbuf = new char[DEFAULT_BUFLEN];
		tSTART = high_resolution_clock::now();
		iResult = recv(new_socket, recvbuf, recvbuflen, 0);
		if (iResult == SOCKET_ERROR){
			printf("receive failed");
			return 1;
		}
		//cout << recvbuf << endl;
		if (iResult>0)
		{
			/*Uncomment from here*/
			fileInx = atoi(recvbuf);
			//printf("Bytes received: %d\n", iResult);
			
			if (fileInx != 0 && fileInx != LastFileInx)
			{
				printf("Load pattern %d \n", fileInx);
				t1 = high_resolution_clock::now();
				okay = okay && sdk.Write_overdrive_image(board_number, bmpArray[fileInx - 1]);  // 12 - 13 ms; clear image on CMOS
				t2 = high_resolution_clock::now();
				auto duration = duration_cast<microseconds>(t2 - t1).count();
				cout << "Write phase mask time (us)" << duration << "\n";
				if (okay){
					cout << "Update phase mask succeed\n";
				}
				else{
					cout << "SLM error\n";
					puts(sdk.Get_last_error_message());
				}

				LastFileInx = fileInx;


			}
			// echo the buffer back to server
			iSendResult = send(new_socket, recvbuf, iResult, 0);
			if (iSendResult == SOCKET_ERROR){
				printf("echo failed");
				return 1;
			}
			//else
				//printf("Bytes sent: %d\n", iSendResult);
			/*Uncomment end */
			
			// write trigger to PV  -- this is currently done in closed-loop interface (RTAOI)
			//DAQmxErrChk(DAQmxWriteAnalogF64(WriteTaskHandle, 10, 1, 0.01, DAQmx_Val_GroupByChannel, WriteData, &written, NULL)); // change channel here to send TTL triggers
			totalWrite += 1;
			cout << "Total Trigger to PV = " << totalWrite << endl;

		}
			tSTOP = high_resolution_clock::now();
			auto tduration = duration_cast<microseconds>(tSTOP - tSTART).count();
			cout << "Loop time (us)" << tduration << "\n";

	} while (iResult > 0);
Error:

	if (DAQmxFailed(error))
		DAQmxGetExtendedErrorInfo(errBuff, 2048);
	printf("DAQmx Error: %s\n", errBuff);
	if (WriteTaskHandle != 0)  {
		/*********************************************/
		// DAQmx Stop Code
		/*********************************************/
		//DAQmxStopTask(taskHandle);
		DAQmxClearTask(WriteTaskHandle);
	}
	if (DAQmxFailed(error))
		printf("DAQmx Error: %s\n", errBuff);
	printf("End of program, press Enter key to quit\n");
	delete[] bmpArray;
	getchar();
	return 0;
}

int32 CVICALLBACK SignalEventCallback(TaskHandle taskHandle, int32 signalID, void *callbackData)
{
	int32       error = 0;
	char        errBuff[2048] = { '\0' };
	static int  totalRead = 0;
	int32       read = 0;
	float64     data[1];
	//printf("This is SignalEventCallback function\n");
	/*********************************************/
	// DAQmx Read Code
	/*********************************************/
	DAQmxErrChk(DAQmxReadAnalogF64(taskHandle, 1, 10.0, DAQmx_Val_GroupByScanNumber, data, 2, &read, NULL));
	if (read > 0) {
		//printf("Acquired %d samples. Total %d\n", read, totalRead += read);
		//fflush(stdout);
	}


Error:
	if (DAQmxFailed(error)) {
		DAQmxGetExtendedErrorInfo(errBuff, 2048);
		/*********************************************/
		// DAQmx Stop Code
		/*********************************************/
		DAQmxStopTask(taskHandle);
		DAQmxClearTask(taskHandle);
		printf("DAQmx Error: %s\n", errBuff);
	}
	return 0;
}

int32 CVICALLBACK DoneCallback(TaskHandle taskHandle, int32 status, void *callbackData)
{
	int32   error = 0;
	char    errBuff[2048] = { '\0' };

	// Check to see if an error stopped the task.
	DAQmxErrChk(status);
	printf("This is DoneCallback function");
Error:
	if (DAQmxFailed(error)) {
		DAQmxGetExtendedErrorInfo(errBuff, 2048);
		DAQmxClearTask(taskHandle);
		printf("DAQmx Error: %s\n", errBuff);
	}
	return 0;
}

bool bmp_08_data_read(std::ifstream &file_in, unsigned long int width,
	long int height, unsigned char *rarray)

	//****************************************************************************80
	//
	//  Purpose:
	//  
	//    BMP_08_DATA_READ reads 8 bit image data of the BMP file.
	// 
	//  Discussion:
	//
	//    On output, the RGB information in the file has been copied into the
	//    R, G and B arrays.
	//
	//    Thanks to Peter Kionga-Kamau for pointing out an error in the
	//    previous implementation.
	//
	//    The standard ">>" operator cannot be used to transfer data, because
	//    it will be deceived by characters that "look like" new lines.
	//
	//    Thanks to Kelly Anderson for pointing out how to modify the program
	//    to handle monochrome images.
	//
	//  Licensing:
	//
	//    This code is distributed under the GNU LGPL license. 
	//
	//  Modified:
	// 
	//    01 April 2005
	//
	//  Author:
	//
	//    Kelly Anderson
	//    John Burkardt
	//
	//  References:
	//
	//    David Kay and John Levine,
	//    Graphics File Formats,
	//    Second Edition,
	//    McGraw Hill, 1995.
	//
	//    Microsoft Corporation,
	//    Microsoft Windows Programmer's Reference,
	//    Volume 5; Messages, Structures, and Macros,
	//    Microsoft Press, 1993.
	//
	//    John Miano,
	//    Compressed Image File Formats,
	//    Addison Wesley, 1999.
	//
	//  Parameters:
	//
	//    Input, ifstream &FILE_IN, a reference to the input file.
	//
	//    Input, unsigned long int WIDTH, the X dimension of the image.
	//
	//    Input, long int HEIGHT, the Y dimension of the image.
	//
	//    Input, unsigned char *RARRAY, a pointer to the red color arrays.
	//
	//    Output, bool BMP_08_DATA_READ, is true if an error occurred.
	//
{
	char c;
	bool error;
	int i;
	unsigned int i2;
	unsigned char *indexr;
	int j;
	int numbyte;
	int padding;
	//
	//  Set the padding.
	//
	padding = (4 - ((1 * width) % 4)) % 4;
	// read header
	char info[54];
	file_in.read(info, 54); // read the 54-byte header

	//// extract image height and width from header
	//int width_header = *(int*)&info[18];
	//int height_header = *(int*)&info[22];

	// read palette and color table
	char palette[1024];
	file_in.read(palette, 1024);

	indexr = rarray;
	numbyte = 0;

	for (j = 0; j < abs(height); j++)
	{
		for (i2 = 0; i2 < width; i2++)
		{
			file_in.read(&c, 1);

			error = file_in.eof();

			if (error)
			{
				//	cout << "\n";
				//	cout << "BMP_08_DATA_READ: Fatal error!\n";
				//	cout << "  Failed reading R for pixel (" << i << "," << j << ").\n";
				return error;
			}

			*indexr = (unsigned char)c;
			int test = *indexr;
			numbyte = numbyte + 1;
			indexr = indexr + 1;
		}
		//
		//  If necessary, read a few padding characters.
		//
		for (i = 0; i < padding; i++)
		{

			file_in.read(&c, 1);

			error = file_in.eof();

			if (error)
			{
				//cout << "\n";
				//cout << "BMP_08_DATA_READ - Warning!\n";
				std::cout << "  Failed while reading padding character " << i << "\n";
				//cout << "  of total " << padding << " characters\n";
				//cout << "  at the end of line " << j << "\n";
				//cout << "\n";
				//cout << "  This is a minor error.\n";
				return false;
			}
		}
	}

	return false;
}

