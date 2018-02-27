# Introduction
This is a software package for the application of online closed-loop all-optical interrogation of neural circuits. The code was written by Zihui Zhang, Lloyd Russell, Oliver Gauld and Adam Packer in the lab of Michael Hausser.

The closed-loop interface (RTAOI) analyses the raw data acquired by a two-photon microscope (Bruker Corporation) on-the-fly and communicates with the microscope control software, Prairie View (Bruker Corporation), a custom spatial light modulator (SLM) control software (CL-Blink, based on the Blink SDK provided by Meadowlark Optics) and a custom sensory stimulation control software (StimPlayground_TCP) via TCP/IP sockets. The interface enable users to select regions of interests (ROIs), specify experiment protocols, and save recordings. 

The UI can work in two modes: display-on and display-off. In the display-on mode, the current calcium image and the calcium traces extracted from the ROIs are displayed on the UI. The display panels should be turned off during experiment for high-speed performance. Three types of experiments can be performed with this package:

1.	__Activity clamp:__
User specifies the target dF/F thresholds and clamping durations. Photostimulation pulses will be triggered if the online recorded dF/F falls below the threshold.

2.	__Trigger-targets:__
User selects one or multiple ‘trigger cells’ and provide phase-masks for the ‘target cells’ assigned to each trigger cell. The activity threshold of each trigger is updated at every frame and compared with the latest calcium signal recorded from the cell. If the signal is above threshold, the software will switch the phase mask on the SLM to target photostimulation beamlets at the corresponding targets cells. 

3.	__Boost sensory response:__
User specifies the type of stimulus and the level of activity thresholds. Stimuli can be triggered manually or in a sequence defined by the user. Photostimulation will be sent to the cell-of-interest if its sensory-evoked activity did not pass the activity threshold within a user-defined timeout window after the delivery of sensory stimuli. 

Detailed instructions can be found in the source codes.
![Closed-loop interface](https://github.com/alloptical/ClosedLoop/blob/master/images/RTAOI201802.PNG)

# System requirements
*	Software has been tested on a Windows 7 desktop.
*	Software platforms used in the package include: VB.net (.NET Framework 4.5), Visual Studio 2013 (64 bit) and MATLAB (2016a). 
*	Access to the raw image data stream depends on PrairieLink (Bruker Corporation) in our implementation, but any microscope acquisition system providing this functionality could potentially be employed.
*	Phase masks are uploaded to the spatial light modulator (OverDrive Plus SLM, Meadowlark Optics) using the Blink_SDK dll (Meadowlark Optics).
*	Analog voltage outputs are generated using NI-DAQmx (version 15.5 National Instruments, device used: USB-6212).
*	(optional) A NVIDIA GPU for online motion correction:
    * Tested with GeForce GTX 750 Ti
    * ManagedCUDA and BitMiracle.LibTiff are used when motion correction is enabled

# Installation guide
*	Install Visual Studio 2013 (with Service Pack 1)
*	Install Measurement Studio (version 15) for Visual Studio 2013 
*	Install NI-DAQmx (tested with version 15.5)
*	Install CUDA toolkit 8.0
*	Install Prairie View 5.4
*	Install .NET Framework 4.5 
*	Connect an analog voltage output (tested with USB-6212, National Instruments) to the photostimulation trigger input.
*	Download the folder 'RTAOI - dev' from Github. Change the device ID and TCP/IP address as instructed in main.vb upon opening the solution. Rebuild the solution.

The installation procedure takes less than 1.5 hours on a desktop computer.

# Instructions for use 
*	For basic use please refer to the instructions to run the demo (below).
*	Detailed experimental protocols can be configured in separate panels.
*	Enable motion correction by loading a reference image (.tiff file) before recording and check 'Use GPU' and 'Get shifts'.
*	If operating in 'Playback' mode, select the saved out .txt file (described below) and click 'Playback' button. Then all preset protocols will be ignored and the specified photostimulation patterns will be delivered at specified frames.
*	Click 'Begin experiment' - 'New recording' - 'Start recording' before starting data recording in Prairie View to ensure the protocol is not affected by previous recordings
*	Save out photostimulation frame and pattern indices into a .txt file (optional).

# Demo 'activity clamp' experiment instructions:
*	Start Prairie View
*	In MarkPoints, import 'gpl_demo.gpl'; load series 'mp_demo.xml'; click 'Run Mark Points'
*	Run CL_Blink.exe; Type number of phase masks (15) when prompted then type 'Y' to start waiting for RTAOI commands
*	Start RTAOI
*	Open 'FOV_with_targets_demo.bmp' and select the marked ROI(s) in the FOV window (in the demo select click from 1 to 4)
*	Configure experiment protocols in the RTAOI interface (e.g. change the threshold etc); check the 'Below' boxes to specify ROIs as trigger cells; click 'lock threshold'; click 'Begin experiment'; click 'Display off' to speed up online processing (optional)
*	Start data streaming in Prairie View

__Expected output:__
Photostimulation will be delivered to the ROIs that have intensity values below the set thresholds.
      
__Expected run time for demo:__
A maximum 2000 photostimulations can be delivered; run time depends on online recorded intensity within the ROIs; the user can terminate the process at anytime by stopping data streaming and restart by clicking 'New recording' in RTAOI.

