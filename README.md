# Introduction
This is a software package for the application of online closed-loop all-optical interrogation of neural circuits. The code was written by Zihui Zhang, Lloyd Russel, Oliver Gauld and Adam Packer in the lab of Michael Hausser.
The closed-loop interface (RTAOI) analyses the raw data acquired by a two-photon microscope (Bruker Corporation) on-the-fly and communicates with the microscope control software, Prairie View (Bruker Corporation), a custom spatial light modulator (SLM) control software (CL-Blink, based on the Blink SDK provided by Meadowlark Optics) and a custom sensory stimulation control software (StimPlayground_TCP) via TCP/IP sockets. The interface enable users to select regions of interests (ROIs), specify experiment protocols, and save recordings. 
The UI can work in two modes: display-on and display-off. In the display-on mode, the current calcium image and the calcium traces extracted from the ROIs are displayed on the UI. The display panels should be turned off during experiment for high-speed performance. Three types of experiments can be performed with this package:

1.	Activity clamp:
User specifies the target dF/F thresholds and clamping durations. Photostimulation pulses will be triggered if the online recorded dF/F falls below the threshold.

2.	Trigger-targets:
User selects one or multiple ‘trigger cells’ and provide phase-masks for the ‘target cells’ assigned to each trigger cell. The activity threshold of each trigger is updated at every frame and compared with the latest calcium signal recorded from the cell. If the signal is above threshold, the software will switch the phase mask on the SLM to target photostimulation beamlets at the corresponding targets cells. 

3.	Boost sensory response
User specifies the type of single-whisker deflection and the level of activity thresholds. Whisker stimuli can be triggered manually or in a sequence defined by the user. Photostimulation will be sent to the cell-of-interest if its sensory-evoked activity did not pass the activity threshold within a user-defined timeout window after the delivery of sensory stimuli. 

Detailed instructions can be found in the source codes.
![Closed-loop interface](https://github.com/alloptical/ClosedLoop/blob/master/images/RTAOI201802.PNG)

# Dependencies
Spiral size, revolution and durations of the photostimulation protocol should be defined in PrarieView (Bruker Corporation). The access to the raw image data stream depends on PrairieLink (Bruker Corporation). The phase masks are uploaded to the SLM using the Blink_SDK dll(Meadowlark Optics). Analogue voltage outputs were generated using NI-DAQmx (National Instruments). Software platforms used in the package include: VB.net, Visual Studio 2013 (64 bits) and Matlab (2016a). This code can be done with Scanimage
http://scanimage.vidriotechnologies.com/pages/viewpage.action?pageId=18252396

