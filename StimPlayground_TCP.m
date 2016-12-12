clear 


num_diff_responses = 3; % lick ports

%global stim params
stimDur = .05; % in seconds
StimFreq = 10; % Hz

MouseID = 'OG98';

% StimParams.Stim.Amp(1,:) = 1; % Volts
% StimParams.Stim.Dur(1,:) = 1; % seconds
% StimParams.Stim.Freq(1,:) = 10; % Hz
% 
% StimParams.Stim.Amp(2,:) = 2; % Volts
% StimParams.Stim.Dur(2,:) = 0.05; % seconds
% StimParams.Stim.Freq(2,:) = 10; % Hz
% 
% StimParams.Stim.Amp(3,:) = 0.8; % Volts
% StimParams.Stim.Dur(3,:) = 0.05; % seconds
% StimParams.Stim.Freq(3,:) = 10; % Hz

StimParams.Stim.Amp(1,:) = 2; % Volts
StimParams.Stim.Dur(1,:) = 1; % seconds
StimParams.Stim.Freq(1,:) = 10; % Hz

StimParams.Stim.Amp(2,:) = 2; % Volts
StimParams.Stim.Dur(2,:) = 0.05; % seconds was 0.05
StimParams.Stim.Freq(2,:) = 40; % Hz

StimParams.Stim.Amp(3,:) =0.8; % Volts   was 0.5 v
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

daq_session = daq.createSession('ni');
daq_session2 = daq.createSession('ni');


%RIG CONFIG
daqID = 'Dev4';
%
% create a TCPIP object.
t = tcpip('0.0.0.0',8070,'NetworkRole','server');
% Wait for connection
fopen(t)
%%

% try -- not used
% addDigitalChannel(daq_session, daqID, 'port1/line0', 'InputOnly'); % arduino Stim1
% addDigitalChannel(daq_session, daqID, 'port1/line1', 'InputOnly'); % arduino Stim2
% addDigitalChannel(daq_session, daqID, 'port1/line2', 'InputOnly'); % arduino Stim3
% addDigitalChannel(daq_session, daqID, 'port1/line3', 'InputOnly'); % arduino Stim4
% addDigitalChannel(daq_session, daqID, 'port1/line4', 'InputOnly'); % arduino Stim5
% -- end not used -- 
addAnalogOutputChannel(daq_session2, daqID,'ao2', 'Voltage');
data = zeros(1000, num_diff_output_lines)';

x = 2;
loop = 0;
stim1counter = 0;
stim2counter = 0;
stim3counter = 0;

while x > 1  % this doesn't work that well and requires psychtoolbox
    A = fread(t, 1); % ACSII for 1,2,3 is 49 50 51
    if(~isempty(A))     
            %         which_stim = find(trigger_states);% this channel went high, so deliver the corresponding stim
            
            which_stim = A;
            if which_stim == 51          % receive 3 for sine stim -- not used 
                data = Xstims{1,1}.waveform;
                 stim1counter = stim1counter+1;
            elseif which_stim == 50      % receive 2 for strong deflection
                data = Xstims{1,2}.waveform;
                stim2counter = stim2counter+1;
            elseif which_stim == 49       % receive 1 for weak deflection
                data = Xstims{1,3}.waveform;
                stim3counter = stim3counter+1;
            end
            data=data';
            queueOutputData(daq_session2, data);
            
            startForeground(daq_session2);
            
            loop = loop+1;
            
            
            data = zeros(1000, num_diff_output_lines);
            %data(:,2) = zeros(length(Xstims{StimParams.Stim2.order(stim2counter),which_stim}.waveform),1);
            
       
    end
end
fclose(t);