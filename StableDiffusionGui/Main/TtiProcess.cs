﻿using StableDiffusionGui.Io;
using StableDiffusionGui.Io;
using StableDiffusionGui.MiscUtils;
using StableDiffusionGui.Os;
using StableDiffusionGui.Ui;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StableDiffusionGui.Main
{
    internal class TtiProcess
    {
        private static bool _hasErrored = false;

        public static void Start()
        {
            _hasErrored = false;
        }

        public static void Finish()
        {
            return; // TODO: Remove this?

            //int imgCount = ImagePreview.SetImages(TextToImage.CurrentTask.OutPath, true, TextToImage.CurrentTask.TargetImgCount);

            //PostProcess(TextToImage.CurrentTask.OutPath, true, TextToImage.CurrentTask.TargetImgCount);

            //if(imgCount > 0)
            //{
            //    Logger.Log($"Done!");
            //}
            //else
            //{
            //    bool logCopySuccess = OsUtils.SetClipboard(Logger.GetSessionLog("sd"));
            //    Logger.Log($"No images generated. {(logCopySuccess ? "Log was copied to clipboard." : "")}");
            //}
            //
            //Program.MainForm.SetWorking(false);
        }

        public static async Task RunStableDiffusion(string[] prompts, string initImg, string embedding, float[] initStrengths, int iterations, int steps, float[] scales, long seed, string sampler, Size res, bool seamless, string outPath)
        {
            // Start(outPath);

            if (File.Exists(initImg))
                initImg = TtiUtils.ResizeInitImg(initImg, res, true);

            long startSeed = seed;

            string promptFilePath = Path.Combine(Paths.GetSessionDataPath(), "prompts.txt");
            List<string> promptFileLines = new List<string>();

            string upscaling = "";
            int upscaleSetting = Config.GetInt("comboxUpscale");

            if (upscaleSetting == 1)
                upscaling = "-U 2";
            else if (upscaleSetting == 2)
                upscaling = "-U 4";

            float gfpganSetting = Config.GetFloat("sliderGfpgan");
            string gfpgan = gfpganSetting > 0.01f ? $"-G {gfpganSetting.ToStringDot("0.00")}" : "";

            foreach (string prompt in prompts)
            {
                for (int i = 0; i < iterations; i++)
                {
                    foreach (float scale in scales)
                    {
                        foreach (float strength in initStrengths)
                        {
                            bool initImgExists = File.Exists(initImg);
                            string init = initImgExists ? $"--init_img {initImg.Wrap()} --strength {strength.ToStringDot("0.0000")}" : "";
                            promptFileLines.Add($"{prompt} {init} -n {1} -s {steps} -C {scale.ToStringDot()} -A {sampler} -W {res.Width} -H {res.Height} -S {seed} {upscaling} {gfpgan} {(seamless ? "--seamless" : "")}");
                            TextToImage.CurrentTask.TargetImgCount++;

                            if (!initImgExists)
                                break;
                        }
                    }

                    seed++;
                }

                if (Config.GetBool(Config.Key.checkboxMultiPromptsSameSeed))
                    seed = startSeed;
            }

            File.WriteAllText(promptFilePath, String.Join("\n", promptFileLines));

            Logger.Log($"Preparing to run Stable Diffusion - {iterations} Iterations, {steps} Steps, Scales {(scales.Length < 4 ? string.Join(", ", scales.Select(x => x.ToStringDot())) : $"{scales.First()}->{scales.Last()}")}, {res.Width}x{res.Height}, Starting Seed: {startSeed}");

            if (!string.IsNullOrWhiteSpace(embedding))
            {
                if (!File.Exists(embedding))
                    embedding = "";
                else
                    Logger.Log($"Using fine-tuned model: {Path.GetFileName(embedding)}");
            }

            string strengths = File.Exists(initImg) ? $" and {initStrengths.Length} strength{(initStrengths.Length != 1 ? "s" : "")}" : "";
            Logger.Log($"{prompts.Length} prompt{(prompts.Length != 1 ? "s" : "")} with {iterations} iteration{(iterations != 1 ? "s" : "")} each and {scales.Length} scale{(scales.Length != 1 ? "s" : "")}{strengths} each = {TextToImage.CurrentTask.TargetImgCount} images total.");

            Process dream = OsUtils.NewProcess(!OsUtils.ShowHiddenCmd());
            TextToImage.CurrentTask.Processes.Add(dream);

            string prec = $"{(Config.GetBool("checkboxFullPrecision") ? "-F" : "")}";

            dream.StartInfo.Arguments = $"{OsUtils.GetCmdArg()} cd /D {Paths.GetDataPath().Wrap()} && call \"{Paths.GetDataPath()}\\mb\\Scripts\\activate.bat\" ldo && " +
                $"python \"{Paths.GetDataPath()}/repo/scripts/dream.py\" --model stable-diffusion-1.4 -o {outPath.Wrap()} --from_file={promptFilePath.Wrap()} {prec}" +
                $"{(!string.IsNullOrWhiteSpace(embedding) ? $"--embedding_path {embedding.Wrap()}" : "")}";

            Logger.Log("cmd.exe " + dream.StartInfo.Arguments, true);

            if (!OsUtils.ShowHiddenCmd())
            {
                dream.OutputDataReceived += (sender, line) => { LogOutput(line.Data); };
                dream.ErrorDataReceived += (sender, line) => { LogOutput(line.Data, true); };
            }

            Logger.Log("Loading...");
            dream.Start();

            if (!OsUtils.ShowHiddenCmd())
            {
                dream.BeginOutputReadLine();
                dream.BeginErrorReadLine();
            }

            while (!dream.HasExited) await Task.Delay(1);

            Finish();
        }

        public static async Task RunStableDiffusionOptimized(string[] prompts, string initImg, float initStrengths, int iterations, int steps, float scale, long seed, Size res, string outPath)
        {
            // Start(outPath);

            if (File.Exists(initImg))
                initImg = TtiUtils.ResizeInitImg(initImg, res, true);

            string promptFilePath = Path.Combine(Paths.GetSessionDataPath(), "prompts.txt");
            string promptFileContent = "";

            // string upscaling = "";
            // int upscaleSetting = Config.GetInt("comboxUpscale");
            // 
            // if (upscaleSetting == 1)
            //     upscaling = "-U 2";
            // else if (upscaleSetting == 2)
            //     upscaling = "-U 4";
            // 
            // float gfpganSetting = Config.GetFloat("sliderGfpgan");
            // string gfpgan = gfpganSetting > 0.01f ? $"-G {gfpganSetting.ToStringDot("0.00")}" : "";

            foreach (string prompt in prompts)
            {
                promptFileContent += $"{prompt}\n";
                TextToImage.CurrentTask.TargetImgCount += iterations;
            }

            File.WriteAllText(promptFilePath, promptFileContent);

            Logger.Log($"Preparing to run Optimized Stable Diffusion - {iterations} Iterations, {steps} Steps, Scale {scale}, {res.Width}x{res.Height}, Starting Seed: {seed}");

            Logger.Log($"{prompts.Length} prompt{(prompts.Length != 1 ? "s" : "")} with {iterations} iteration{(iterations != 1 ? "s" : "")} each = {TextToImage.CurrentTask.TargetImgCount} images total.");

            Process dream = OsUtils.NewProcess(!OsUtils.ShowHiddenCmd());
            TextToImage.CurrentTask.Processes.Add(dream);

            string prec = $"{(Config.GetBool("checkboxFullPrecision") ? "full" : "autocast")}";

            dream.StartInfo.Arguments = $"{OsUtils.GetCmdArg()} cd /D {Paths.GetDataPath().Wrap()} && call \"{Paths.GetDataPath()}\\mb\\Scripts\\activate.bat\" ldo && " +
                $"python \"{Paths.GetDataPath()}/repo/optimizedSD/optimized_txt2img.py\" --model stable-diffusion-1.4 --outdir {outPath.Wrap()} --from-file {promptFilePath.Wrap()} --n_iter {iterations} " +
                $"--ddim_steps {steps} --W {res.Width} --H {res.Height} --scale {scale.ToStringDot("0.0000")} --seed {seed} --precision {prec}";

            Logger.Log("cmd.exe " + dream.StartInfo.Arguments, true);

            if (!OsUtils.ShowHiddenCmd())
            {
                dream.OutputDataReceived += (sender, line) => { LogOutput(line.Data); };
                dream.ErrorDataReceived += (sender, line) => { LogOutput(line.Data, true); };
            }

            Logger.Log("Loading...");
            dream.Start();

            if (!OsUtils.ShowHiddenCmd())
            {
                dream.BeginOutputReadLine();
                dream.BeginErrorReadLine();
            }

            while (!dream.HasExited) await Task.Delay(1);

            Finish();
        }

        public static async Task RunStableDiffusionCli(string outPath)
        {
            if (Program.Busy)
                return;

            string batPath = Path.Combine(Paths.GetSessionDataPath(), "dream.bat");

            string batText = $"@echo off\n title Dream.py CLI && cd /D {Paths.GetDataPath().Wrap()} && call \"mb\\Scripts\\activate.bat\" \"mb/envs/ldo\" && " +
                $"python \"repo/scripts/dream.py\" --model stable-diffusion-1.4 -o {outPath.Wrap()} {(Config.GetBool("checkboxFullPrecision") ? "--full_precision" : "")}";

            File.WriteAllText(batPath, batText);
            Process.Start(batPath);
        }

        static void LogOutput(string line, bool stdErr = false)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            //Stopwatch sw = new Stopwatch();
            //sw.Restart();

            //lastLogName = ai.LogFilename;
            Logger.Log(line, true, false, "sd");

            bool ellipsis = Logger.LastUiLine.Contains("...");

            if (TextToImage.LastTaskSettings != null && TextToImage.LastTaskSettings.Implementation == Data.Implementation.StableDiffusion)
            {
                bool replace = ellipsis || Logger.LastUiLine.MatchesWildcard("*Generated*image*in*");

                if (line.Contains("Setting Sampler"))
                {
                    Logger.Log("Generating...");
                    Program.MainForm.SetProgress((int)Math.Round(((float)1 / TextToImage.CurrentTask.TargetImgCount) * 100f));
                }

                if (line.Contains("image(s) generated in "))
                {
                    var split = line.Split("image(s) generated in ");
                    TextToImage.CurrentTask.ImgCount += split[0].GetInt();
                    Program.MainForm.SetProgress((int)Math.Round(((float)(TextToImage.CurrentTask.ImgCount + 1) / TextToImage.CurrentTask.TargetImgCount) * 100f));

                    int lastMsPerImg = $"{split[1].Remove(".").Remove("s")}0".GetInt();
                    int remainingMs = (TextToImage.CurrentTask.TargetImgCount - TextToImage.CurrentTask.ImgCount) * lastMsPerImg;

                    string lastLine = Logger.LastUiLine;

                    Logger.Log($"Generated {split[0].GetInt()} image in {split[1]} ({TextToImage.CurrentTask.ImgCount}/{TextToImage.CurrentTask.TargetImgCount})" +
                        $"{(TextToImage.CurrentTask.ImgCount > 1 && remainingMs > 1000 ? $" - ETA: {FormatUtils.Time(remainingMs, false)}" : "")}", false, replace || Logger.LastUiLine.MatchesWildcard("*Generated*image*in*"));
                    // ImagePreview.SetImages(TextToImage.CurrentTask.OutPath, true, TextToImage.CurrentTask.ImgCount);
                }
            }

            if (TextToImage.LastTaskSettings != null && TextToImage.LastTaskSettings.Implementation == Data.Implementation.StableDiffusionOptimized)
            {
                bool replace = ellipsis || Logger.LastUiLine.MatchesWildcard("*Generated*image*in*");

                if (line.Contains("reading prompts from"))
                {
                    Logger.Log("Generating...");
                    Program.MainForm.SetProgress((int)Math.Round(((float)1 / TextToImage.CurrentTask.TargetImgCount) * 100f));
                }

                if (line.Contains("Decoding image: "))
                {
                    int percent = line.Split("Decoding image: ")[1].Split('#')[0].GetInt();

                    if(percent > 0 && percent <= 100)
                        Logger.Log($"Generating... {percent}%", false, replace);
                }

                if (line.MatchesWildcard("*data: 100%*<00:00,*it*]"))
                {
                    TextToImage.CurrentTask.ImgCount += 1;
                    Program.MainForm.SetProgress((int)Math.Round(((float)(TextToImage.CurrentTask.ImgCount + 1) / TextToImage.CurrentTask.TargetImgCount) * 100f));

                    int lastMsPerImg = line.EndsWith("it/s]") ? (1000000f / (line.Split("00:00, ").Last().Remove(".").Remove("s") + "0").GetInt()).RoundToInt() : (line.Split("00:00, ").Last().Remove(".").Remove("s") + "0").GetInt();
                    int remainingMs = (TextToImage.CurrentTask.TargetImgCount - TextToImage.CurrentTask.ImgCount) * lastMsPerImg;

                    string lastLine = Logger.LastUiLine;

                    Logger.Log($"Generated 1 image in {FormatUtils.Time(lastMsPerImg, false)} ({TextToImage.CurrentTask.ImgCount}/{TextToImage.CurrentTask.TargetImgCount})" +
                        $"{(TextToImage.CurrentTask.ImgCount > 1 && remainingMs > 1000 ? $" - ETA: {FormatUtils.Time(remainingMs, false)}" : "")}", false, replace || Logger.LastUiLine.MatchesWildcard("Generated*image*"));
                    // ImagePreview.SetImages(TextToImage.CurrentTask.OutPath, true, TextToImage.CurrentTask.ImgCount);
                }
            }


            if (line.MatchesWildcard("*%|*/*[*B/s]*") && !line.ToLower().Contains("it/s") && !line.ToLower().Contains("s/it"))
            {
                Logger.Log($"Downloading required files... {line.Trunc(80)}", false, ellipsis);
            }

            //if (line.Contains("Generating: 100%"))
            //{
            //    Logger.Log($"Post-processing...", false, replace);
            //}

            // if (line.Contains("Restoring Faces"))
            // {
            //     Logger.Log($"Restoring faces...", false, replace);
            // }
            // 
            // if (line.MatchesWildcard("*Tile */*"))
            // {
            //     Logger.Log($"Upscaling...", false, replace);
            // }

            string lastLogLines = string.Join("\n", Logger.GetSessionLogLastLines("sd", 6).Select(x => $"[{x.Split("]: [").Skip(1).FirstOrDefault()}"));

            if (!_hasErrored && (line.Contains("RuntimeError") || line.Contains("ImportError") || line.Contains("OSError")))
            {
                _hasErrored = true;
                UiUtils.ShowMessageBox($"Python Error:\n\n{lastLogLines}", UiUtils.MessageType.Error);
            }

            if (!_hasErrored && (line.Contains("PytorchStreamReader failed reading zip archive") || line.Contains("UnpicklingError")))
            {
                _hasErrored = true;
                UiUtils.ShowMessageBox($"Your model file seems to be damaged or incomplete!\n\n{lastLogLines}", UiUtils.MessageType.Error);
            }

            if (!_hasErrored && line.Contains("usage: "))
            {
                _hasErrored = true;
                UiUtils.ShowMessageBox($"Invalid CLI syntax.", UiUtils.MessageType.Error);
            }

            if (!_hasErrored && line.ToLower().Contains("cuda out of memory"))
            {
                _hasErrored = true;
                UiUtils.ShowMessageBox($"Your GPU ran out of VRAM! Try a lower resolution.\n\n{line}", UiUtils.MessageType.Error);
            }

            if (!_hasErrored && line.ToLower().Contains("illegal memory access"))
            {
                _hasErrored = true;
                UiUtils.ShowMessageBox($"Your GPU appears to be unstable! If you have an overclock enabled, please disable it!\n\n{line}", UiUtils.MessageType.Error);
            }

            if (!_hasErrored && line.ToLower().Contains("0 image(s) generated in"))
            {
                _hasErrored = true;
                UiUtils.ShowMessageBox($"An unknown error occured. Check the log for details:!\n\n{lastLogLines}", UiUtils.MessageType.Error);
            }

            if (_hasErrored)
                TextToImage.Cancel();
        }

        public static void Kill()
        {
            if (TextToImage.CurrentTask != null)
            {
                foreach (var process in TextToImage.CurrentTask.Processes.Where(x => x != null && !x.HasExited))
                {
                    try
                    {
                        OsUtils.KillProcessTree(process.Id);
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Failed to kill process tree: {e.Message}", true);
                    }
                }
            }
        }
    }
}
