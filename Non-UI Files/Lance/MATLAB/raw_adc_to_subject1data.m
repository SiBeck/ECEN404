clear; clc; close all;

%% ================= USER SETTINGS =================
binFile = 'adc_data.bin';

% Output files for STM32CubeIDE
outC = 'subject1_data.c';
outH = 'subject1_data.h';

% Radar capture settings from mmWave Studio
numADCSamples   = 96;
numRX           = 4;
numChirpsPerLoop = 3;     % chirps 0,1,2
numLoopsPerFrame = 48;    % from frame config
numFramesToUse   = [];    % [] = use all complete frames

adcBits = 16;             % 16-bit ADC
isComplex = true;         % Complex1x
iqOrder = 'I_FIRST';      % I First

% Profile settings
fs_adc = 10.785e6;        % ADC sample rate in Hz
slope_Hz_per_s = 199.987e12;  % 199.987 MHz/us = 199.987e12 Hz/s
c = 3e8;

% Chest search interval
rangeMin_m = 0.2;
rangeMax_m = 0.5;

% Choose one chirp stream for slow-time extraction
% 1 = chirp 0, 2 = chirp 1, 3 = chirp 2
chosenChirp = 2;

% Choose RX channel automatically from strongest mean energy in chest range
autoPickRX = true;
forcedRX = 1;   % used only if autoPickRX = false

% Range FFT length
NfftRange = 256;

% Slow-time output sample rate for the chosen chirp stream
% One sample per loop, loops repeat every framePeriod/numLoopsPerFrame
framePeriod_s = 70e-3;
slowTimeFs = numLoopsPerFrame / framePeriod_s;   % ~685.714 Hz

% Optional decimation of extracted slow-time signal before export
applySlowTimeDecimation = true;
slowTimeDecim = 8;   % adjust as needed

% Max exported samples to keep MCU data manageable
maxExportSamples = 3000;

%% ================= READ RAW ADC FILE =================
fid = fopen(binFile, 'rb');
if fid < 0
    error('Could not open %s', binFile);
end

raw = fread(fid, inf, 'int16=>double');
fclose(fid);

if isempty(raw)
    error('Binary file is empty or unreadable.');
end

fprintf('Read %d int16 values from %s\n', numel(raw), binFile);

%% ================= DECODE COMPLEX ADC =================
if ~isComplex
    error('This script currently expects Complex1x data.');
end

if mod(numel(raw), 2) ~= 0
    warning('Odd number of int16 values found. Dropping the last one.');
    raw = raw(1:end-1);
end

switch upper(iqOrder)
    case 'I_FIRST'
        I = raw(1:2:end);
        Q = raw(2:2:end);
    otherwise
        error('Only I_FIRST is implemented in this script.');
end

adcComplex = complex(I, Q);
numComplexSamples = numel(adcComplex);

%% ================= RESHAPE INTO [ADC, RX, CHIRP, LOOP, FRAME] =================
samplesPerLoop = numADCSamples * numRX * numChirpsPerLoop;
samplesPerFrame = samplesPerLoop * numLoopsPerFrame;

numCompleteFrames = floor(numComplexSamples / samplesPerFrame);
if numCompleteFrames < 1
    error('Not enough data for even one complete frame.');
end

if isempty(numFramesToUse)
    numFrames = numCompleteFrames;
else
    numFrames = min(numFramesToUse, numCompleteFrames);
end

adcComplex = adcComplex(1:numFrames * samplesPerFrame);

% Assumption:
% Fastest dimension in stored data is ADC sample, then RX, then chirp, then loop, then frame
data = reshape(adcComplex, ...
    [numADCSamples, numRX, numChirpsPerLoop, numLoopsPerFrame, numFrames]);

fprintf('Using %d complete frame(s)\n', numFrames);
fprintf('Data cube size = [%d ADC, %d RX, %d chirps, %d loops, %d frames]\n', ...
    size(data,1), size(data,2), size(data,3), size(data,4), size(data,5));

%% ================= SELECT ONE CHIRP STREAM =================
if chosenChirp < 1 || chosenChirp > numChirpsPerLoop
    error('chosenChirp must be between 1 and %d', numChirpsPerLoop);
end

% dataChirp: [ADC, RX, LOOP, FRAME]
dataChirp = squeeze(data(:,:,chosenChirp,:,:));

%% ================= RANGE FFT =================
winRange = hann(numADCSamples, 'periodic');
rangeFFT = zeros(NfftRange, numRX, numLoopsPerFrame, numFrames);

for f = 1:numFrames
    for lp = 1:numLoopsPerFrame
        for rx = 1:numRX
            x = dataChirp(:,rx,lp,f);
            X = fft(x .* winRange, NfftRange);
            rangeFFT(:,rx,lp,f) = X;
        end
    end
end

% Keep positive half only
numPosBins = NfftRange/2;
rangeFFT = rangeFFT(1:numPosBins,:,:,:);

% Range axis
freqBeat = (0:numPosBins-1).' * (fs_adc / NfftRange);
rangeAxis_m = c * freqBeat / (2 * slope_Hz_per_s);

%% ================= FIND CHEST RANGE WINDOW =================
roiBins = find(rangeAxis_m >= rangeMin_m & rangeAxis_m <= rangeMax_m);
if isempty(roiBins)
    error('No range bins fall inside %.3f m to %.3f m. Increase NfftRange or widen interval.', ...
        rangeMin_m, rangeMax_m);
end

%% ================= PICK RX CHANNEL =================
meanMagPerRx = zeros(numRX,1);

for rx = 1:numRX
    tmp = abs(rangeFFT(roiBins, rx, :, :));
    meanMagPerRx(rx) = mean(tmp(:));
end

if autoPickRX
    [~, chosenRX] = max(meanMagPerRx);
else
    chosenRX = forcedRX;
end

fprintf('Chosen chirp index = %d\n', chosenChirp);
fprintf('Chosen RX channel  = %d\n', chosenRX-1);  % print as Rx0/Rx1/... style

%% ================= FIND BEST RANGE BIN =================
roiData = abs(rangeFFT(roiBins, chosenRX, :, :));
meanRangeProfile = mean(roiData, [3 4]);
meanRangeProfile = squeeze(meanRangeProfile);

[~, idxLocal] = max(meanRangeProfile);
chosenBin = roiBins(idxLocal);
chosenRange_m = rangeAxis_m(chosenBin);

fprintf('Chosen chest bin = %d\n', chosenBin-1);
fprintf('Chosen chest range ≈ %.4f m\n', chosenRange_m);

%% ================= EXTRACT SLOW-TIME COMPLEX SIGNAL =================
% One complex sample per loop per frame, from the chosen range bin / rx / chirp
slowSig = squeeze(rangeFFT(chosenBin, chosenRX, :, :));   % [loops, frames]
slowSig = slowSig(:);                                     % column vector, slow-time sequence

% Remove DC
slowSig = slowSig - mean(slowSig);

% Extract unwrapped phase
phi = unwrap(angle(slowSig));

%% ================= OPTIONAL SLOW-TIME DECIMATION =================
fs_out = slowTimeFs;

if applySlowTimeDecimation && slowTimeDecim > 1
    slowSig = decimate(slowSig, slowTimeDecim);
    fs_out = slowTimeFs / slowTimeDecim;
end

% Recompute phase after any decimation
phi = unwrap(angle(slowSig));

% Simple bandpass filters
respSig = bandpass(phi, [0.1 0.5], fs_out);
heartSig = bandpass(phi, [0.8 2.0], fs_out);

%% ================= SAVE FULL HEART WAVEFORM FROM ENTIRE .BIN =================
t_full = (0:length(heartSig)-1).' / fs_out;
HB_full = table(t_full, heartSig(:), 'VariableNames', {'t_s','hb'});
writetable(HB_full, 'heartbeat_waveform_full_bin.csv');
disp("Full-bin heartbeat waveform saved to heartbeat_waveform_full_bin.csv");

%% ================= LIMIT LENGTH FOR MCU EXPORT =================
N = numel(slowSig);
if N > maxExportSamples
    slowSig = slowSig(1:maxExportSamples);
    N = maxExportSamples;
end

I_out = real(slowSig);
Q_out = imag(slowSig);

fprintf('Exporting %d slow-time IQ samples\n', N);
fprintf('Output slow-time Fs = %.6f Hz\n', fs_out);



%% ================= LIVE TESTING SEND TO UART =================
port = "COM14";      % change to your USB-UART COM port
baud = 115200;       % must match STM32 huart3.Init.BaudRate

s = serialport(port, baud);
flush(s);
pause(2);            % give port time to open/reset

count = uint32(length(I_out));
fs_hz = single(fs_out);

% Send header
write(s, typecast(count, 'uint8'), 'uint8');
write(s, typecast(fs_hz, 'uint8'), 'uint8');

% Send IQ samples
for k = 1:double(count)
    write(s, typecast(single(I_out(k)), 'uint8'), 'uint8');
    write(s, typecast(single(Q_out(k)), 'uint8'), 'uint8');
end

disp("Dataset sent.");
disp("Listening for STM output...");

% Collect full STM printout
logText = "";
idleTimeout_s = 5;      % stop after 5 s of no new UART data
hardTimeout_s = 120;    % absolute max wait time
t0 = tic;
tLast = tic;

while toc(t0) < hardTimeout_s
    nAvail = s.NumBytesAvailable;
    if nAvail > 0
        data = read(s, nAvail, "uint8");
        txt = char(data(:)).';
        fprintf('%s', txt);          % display everything live in MATLAB
        logText = logText + string(txt);
        tLast = tic;
    else
        pause(0.05);
    end

    if toc(tLast) > idleTimeout_s
        break;
    end
end

clear s

% Save full raw STM UART log
fullLogFile = "stm_uart_log.txt";
fid = fopen(fullLogFile, "w");
fprintf(fid, "%s", char(logText));
fclose(fid);
disp("Full STM output saved to " + fullLogFile);

%% ================= SAVE ONLY VERIFICATION LINES TO TXT =================
allText = char(logText);

% Normalize line endings
allText = regexprep(allText, '\r\n|\r|\n', newline);

% Remove trigger CSV block completely
allText = regexprep(allText, ...
    'TRIG_CSV_BEGIN[\s\S]*?TRIG_CSV_END', ...
    '');

% Remove heartbeat CSV block completely
allText = regexprep(allText, ...
    'HB_CSV_BEGIN[\s\S]*?HB_CSV_END', ...
    '');

% Split into individual lines
lines = splitlines(string(allText));
lines = strtrim(lines);

% Remove empty lines
lines(lines == "") = [];

% main verification-style print lines
keepMask = startsWith(lines, "RX OK:") | ...
           startsWith(lines, "MCU OUTPUT TERMINAL") | ...
           startsWith(lines, "VALID:") | ...
           startsWith(lines, "Fs =") | ...
           startsWith(lines, "RMS:") | ...
           startsWith(lines, "Peak:") | ...
           startsWith(lines, "IMG_CAL:") | ...
           startsWith(lines, "IMG_CAL2:") | ...
           startsWith(lines, "DATASET:") | ...
           startsWith(lines, "CHECK:") | ...
           startsWith(lines, "TRIG_SUM:") | ...
           startsWith(lines, "TRIG_CHECK:");

verifyLines = lines(keepMask);

% Write to txt file
verifyFile = "stm_verification_only.txt";
fid = fopen(verifyFile, "w");
if fid < 0
    error("Could not create %s", verifyFile);
end

for k = 1:numel(verifyLines)
    fprintf(fid, "%s\n", verifyLines(k));
end
fclose(fid);

disp("Verification-only lines saved to " + verifyFile);

%% ================= EXTRACT TRIGGER CSV BLOCK =================
allText = char(logText);

startTag = 'TRIG_CSV_BEGIN';
endTag   = 'TRIG_CSV_END';

i1 = strfind(allText, startTag);
i2 = strfind(allText, endTag);

if isempty(i1) || isempty(i2) || i2(1) <= i1(1)
    warning('Could not find trigger CSV block in STM output.');
else
    block = allText(i1(1)+length(startTag):i2(1)-1);

    % Normalize line endings and split
    block = regexprep(block, '\r\n|\r|\n', newline);
    lines = splitlines(string(block));
    lines = strtrim(lines);
    lines(lines == "") = [];

    % Expect first non-empty line to be header: t_trig_s,amp
    if numel(lines) >= 1
        dataLines = lines(2:end);

        t_vals = [];
        amp_vals = [];

        for idx = 1:numel(dataLines)
            parts = split(dataLines(idx), ',');
            if numel(parts) ~= 2
                continue;
            end

            tnum = str2double(strtrim(parts(1)));
            anum = str2double(strtrim(parts(2)));

            if ~isnan(tnum) && ~isnan(anum)
                t_vals(end+1,1) = tnum; %#ok<SAGROW>
                amp_vals(end+1,1) = anum; %#ok<SAGROW>
            end
        end

        T = table(t_vals, amp_vals, 'VariableNames', {'t_trig_s','amp'});
        writetable(T, 'trigger_cycles.csv');
        disp("Trigger cycles saved to trigger_cycles.csv");
        disp(T);
    else
        warning('Trigger CSV block was found but empty.');
    end
end

%% ================= EXTRACT HEARTBEAT WAVEFORM CSV BLOCK =================
allText = char(logText);

startTag = 'HB_CSV_BEGIN';
endTag   = 'HB_CSV_END';

i1 = strfind(allText, startTag);
i2 = strfind(allText, endTag);

if isempty(i1) || isempty(i2) || i2(1) <= i1(1)
    warning('Could not find heartbeat waveform CSV block in STM output.');
else
    block = allText(i1(1)+length(startTag):i2(1)-1);

    % Normalize line endings and split
    block = regexprep(block, '\r\n|\r|\n', newline);
    lines = splitlines(string(block));
    lines = strtrim(lines);
    lines(lines == "") = [];

    % Expect first non-empty line to be header: t_s,hb
    if numel(lines) >= 1
        dataLines = lines(2:end);

        t_hb = [];
        hb_vals = [];

        for idx = 1:numel(dataLines)
            parts = split(dataLines(idx), ',');
            if numel(parts) ~= 2
                continue;
            end

            tnum = str2double(strtrim(parts(1)));
            hnum = str2double(strtrim(parts(2)));

            if ~isnan(tnum) && ~isnan(hnum)
                t_hb(end+1,1) = tnum; %#ok<SAGROW>
                hb_vals(end+1,1) = hnum; %#ok<SAGROW>
            end
        end

        HB = table(t_hb, hb_vals, 'VariableNames', {'t_s','hb'});
        writetable(HB, 'heartbeat_waveform.csv');
        disp("Heartbeat waveform saved to heartbeat_waveform.csv");
        disp(HB);
    else
        warning('Heartbeat waveform CSV block was found but empty.');
    end
end

