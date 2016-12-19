% Instructions for running:
%  1. setup stim duration, amplitude and frequency for different stim types
%  2. setup daq device index and TCP address
%  3. run the script and the voltage waveform specified by the number in socket will be used for driving the piezo controller

clear 
%global stim params
stimDur = .05; % in seconds

% search sensory-responsive cells
StimParams.Stim.Amp(1,:) = 2; % Volts
StimParams.Stim.Dur(1,:) = 1; % seconds
StimParams.Stim.Freq(1,:) = 10; % Hz

% strong deflection
StimParams.Stim.Amp(2,:) = 2; % Volts
StimParams.Stim.Dur(2,:) = 0.05; % seconds 
StimParams.Stim.Freq(2,:) = 40; % Hz

% weak deflection
StimParams.Stim.Amp(3,:) =0.8; % Volts   
StimParams.Stim.Dur(3,:) = 0.05; % seconds
StimParams.Stim.Freq(3,:) = 40; % Hz

numdiff_freqs = length(StimParams.Stim.Amp);

for var = 1:1
    for stim = 1:numdiff_freqs
        % X plane
        Xstims{var,stim}.device         = 1;
        Xstims{var,stim}.output_line    = 1; % 0 OR 1
        Xstims{var,stim}.sample_rate_hz = 1000;
        Xstims{var,stim}.frequency_hz   = StimParams.Stim.Freq(stim,:);% 25 OR 5
        Xstims{var,stim}.duration_s     = StimParams.Stim.Dur(stim,:);
        Xstims{var,stim}.amplitude_v    = StimParams.Stim.Amp(stim,:);
        Xstims{var,stim}.waveform       = sin(2*pi*Xstims{var,stim}.frequency_hz* ...
                                    [0:1/Xstims{var,stim}.sample_rate_hz:Xstims{var,stim}.duration_s-(1/Xstims{var,stim}.sample_rate_hz)] ...
                                     ) * Xstims{var,stim}.amplitude_v;
        Xstims{var,stim}.waveform(1+(Xstims{var,stim}.duration_s*1000):1000) = 0;                        
    
    
        subplot(3,1,stim)
        plot(Xstims{var,stim}.waveform)
        xlabel('time ms'); ylabel('Voltage Amps');
        title(['Stim ' num2str(stim)])
    end
    
end
num_diff_stims          = length(Xstims);
sample_rate_hz          = 1000;  % could search for this in stims struct
num_diff_output_lines   = 1;  % could search for this in stims struct
longest_stim_duration_s = stimDur;  % could search for this in stims struct

%daq config
daq_session = daq.createSession('ni');
daqID = 'Dev4'; % change daq device index here

% create a TCPIP object. 
t = tcpip('0.0.0.0',8070,'NetworkRole','server'); % change TCPIP address here
% wait for client connection
fopen(t)
%%
addAnalogOutputChannel(daq_session, daqID,'ao2', 'Voltage');
data = zeros(1000, num_diff_output_lines)';

x = 2;
loop = 0;
stim1counter = 0;
stim2counter = 0;
stim3counter = 0;

while x > 1 
    A = fread(t, 1); % ACSII for 1,2,3 is 49 50 51
    if(~isempty(A))      
            which_stim = A;
            if which_stim == 51          % receive 3 for sine stim -- not used 
                data = Xstims{1,1}.waveform;
                 stim1counter = stim1counter+1;
            elseif which_stim == 50      % receive 2 for strong deflection
                data = Xstims{1,2}.waveform;
                stim2counter = stim2counter+1;
            elseif which_stim == 49      % receive 1 for weak deflection
                data = Xstims{1,3}.waveform;
                stim3counter = stim3counter+1;
            end
            data=data';
            queueOutputData(daq_session, data);
            startForeground(daq_session);
            loop = loop+1;
            data = zeros(1000, num_diff_output_lines);
   
    end
end
fclose(t);