using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using UnityEngine.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Runtime.Serialization.Formatters.Binary;
using KKAPI;
using YamlDotNet;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Linq;
using System.Xml;
using static MaterialEditorAPI.MaterialAPI;
using static MaterialEditorAPI.MaterialEditorPluginBase;

namespace KK_Plugins
{
    /// <summary>
    /// Random stuff
    /// </summary>
    
    
    
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    [BepInPlugin(GUID, PluginName, Version)]

    public class MaterialExport : BaseUnityPlugin
    {
        public const string GUID = "com.masked.bepinex.materialexporter";
        public const string PluginName = "Material Exporter";
        public const string PluginNameInternal = "MaterialExport";
        public const string Version = "1.0";



        public string CharacterName;
        internal static new ManualLogSource Logger;
        //config stuff
        internal static ConfigEntry<string> ConfigExportPath { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ExportKeyBind { get; private set; }
        public Material WhatWeExport;
        public static string ExportPathDefault = Path.Combine(Paths.GameRootPath, @"UserData\MaterialExporter");
        public static string ExportPath;
        //Material info

        //Body
        public string[] Properties;
        public string[] Floats;
        public string[] Colors;
        private void Start()
        {
            // = BepInEx.Logging.Logger.CreateLogSource("MaskedMaterInfo");
            Logger.LogMessage("Started Material Exporter");
            WriteConfig();
            LoadXML();

        }

        //XML parser
        public static SortedDictionary<string, Dictionary<string, ShaderPropertyData>> XMLShaderProperties = new SortedDictionary<string, Dictionary<string, ShaderPropertyData>>();
        private static void LoadXML()
        {
            Logger.LogMessage($"Attempting to real XML at {Path.Combine(Paths.GameRootPath, @"UserData\MaterialExporter\ShaderProperties.xml")}");
            XMLShaderProperties["default"] = new Dictionary<string, ShaderPropertyData>();
            
            using (var stream = new StreamReader(Path.Combine(Paths.GameRootPath, @"UserData\MaterialExporter\ShaderProperties.xml"),true)) //this is temporary, will embed this into the assembly in the future
                if (stream != null)
                {
                    try
                    {
                        using (XmlReader reader = XmlReader.Create(stream))
                        {

                            XmlDocument doc = new XmlDocument();
                            doc.LoadXml(File.ReadAllText(Path.Combine(Paths.GameRootPath, @"UserData\MaterialExporter\ShaderProperties.xml")));
                            //doc.Load(stream);
                            XmlElement materialEditorElement = doc.DocumentElement;

                            var shaderElements = materialEditorElement.GetElementsByTagName("Shader");
                            foreach (var shaderElementObj in shaderElements)
                            {
                                if (shaderElementObj != null)
                                {
                                    var shaderElement = (XmlElement)shaderElementObj;
                                    {
                                        string shaderName = shaderElement.GetAttribute("Name");

                                        XMLShaderProperties[shaderName] = new Dictionary<string, ShaderPropertyData>();

                                        var shaderPropertyElements = shaderElement.GetElementsByTagName("Property");
                                        foreach (var shaderPropertyElementObj in shaderPropertyElements)
                                        {
                                            if (shaderPropertyElementObj != null)
                                            {
                                                var shaderPropertyElement = (XmlElement)shaderPropertyElementObj;
                                                {
                                                    string propertyName = shaderPropertyElement.GetAttribute("Name");
                                                    ShaderPropertyType propertyType = (ShaderPropertyType)Enum.Parse(typeof(ShaderPropertyType), shaderPropertyElement.GetAttribute("Type"));
                                                    Logger.LogInfo($"_{propertyName}");
                                                    if (propertyType == ShaderPropertyType.Texture && !Props_Tex.Contains($"_{propertyName}"))
                                                    {
                                                        Props_Tex.Add($"_{propertyName}");
                                                        Logger.LogMessage($"Added _{propertyName} to Texture List");
                                                    }
                                                    else if (propertyType == ShaderPropertyType.Color && !Props_Color.Contains($"_{propertyName}"))
                                                    {
                                                        Props_Color.Add($"_{propertyName}");
                                                        Logger.LogMessage($"Added _{propertyName} to Color List");
                                                    }
                                                    else if (propertyType == ShaderPropertyType.Float && !Props_float.Contains($"_{propertyName}"))
                                                    {
                                                        Props_float.Add($"_{propertyName}");
                                                        Logger.LogMessage($"Added _{propertyName} to Float List");
                                                    }
                                                    string defaultValue = shaderPropertyElement.GetAttribute("DefaultValue");
                                                    string defaultValueAB = shaderPropertyElement.GetAttribute("DefaultValueAssetBundle");
                                                    string range = shaderPropertyElement.GetAttribute("Range");
                                                    string min = null;
                                                    string max = null;
                                                    if (!range.IsNullOrWhiteSpace())
                                                    {
                                                        var rangeSplit = range.Split(',');
                                                        if (rangeSplit.Length == 2)
                                                        {
                                                            min = rangeSplit[0];
                                                            max = rangeSplit[1];
                                                        }
                                                    }
                                                    ShaderPropertyData shaderPropertyData = new ShaderPropertyData(propertyName, propertyType, defaultValue, defaultValueAB, min, max);

                                                    XMLShaderProperties["default"][propertyName] = shaderPropertyData;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch(Exception e)
                    {

                        Logger.LogError(e);
                    }

                }
            else
                {
                    Logger.LogMessage("ERROR: Stream is null for some reason, does the file exist?");
                }
        }


        private bool IsValidPath(string path)
        {
            // Check if the path is rooted in a driver

            if (path == null || path.Length < 3) return false;
            Regex driveCheck = new Regex(@"^[a-zA-Z]:\\$");
            if (!driveCheck.IsMatch(path.Substring(0, 3))) return false;

            // Check if such driver exists
            IEnumerable<string> allMachineDrivers = DriveInfo.GetDrives().Select(drive => drive.Name);
            if (!allMachineDrivers.Contains(path.Substring(0, 3))) return false;

            // Check if the rest of the path is valid
            string InvalidFileNameChars = new string(Path.GetInvalidPathChars());
            InvalidFileNameChars += @":/?*" + "\"";
            Regex containsABadCharacter = new Regex("[" + Regex.Escape(InvalidFileNameChars) + "]");
            if (containsABadCharacter.IsMatch(path.Substring(3, path.Length - 3)))
                return false;
            if (path[path.Length - 1] == '.') return false;

            return true;
        }
        private void SetExportPath()
        {
            if (ConfigExportPath.Value == "")
                ExportPath = ExportPathDefault;
            else
            {
                if(IsValidPath(ConfigExportPath.Value))
                {
                    ExportPath = ConfigExportPath.Value;
                }
                else
                {
                    Logger.LogMessage($"{ConfigExportPath.Value} Is not a valid path, Falling back to internal default");
                    ExportPath = ExportPathDefault;
                }
                
                


            }
            
        }
        internal virtual void ConfigExportPath_SettingChanged(object sender, EventArgs e)
        {
            SetExportPath();
        }
        public void WriteConfig()
        {
            Logger.LogMessage("Attempting To Write Config File");

            ConfigExportPath = Config.Bind("Config", "Export Path Override", "", new ConfigDescription($"Materials will be exported to this folder. If empty, exports to {ExportPathDefault}", null, new ConfigurationManagerAttributes { Order = 1 }));
            ExportKeyBind = Config.Bind("Keyboard Shortcuts", "Export Keybind", new KeyboardShortcut(KeyCode.M, KeyCode.LeftShift), "Export Materials when the keybind is pressed");
            ConfigExportPath.SettingChanged += ConfigExportPath_SettingChanged;
            SetExportPath();
            if (!Directory.Exists(ExportPath))
            {
                Logger.LogMessage("Creating Export Directory: " + ExportPath);
                Directory.CreateDirectory(ExportPath);
            }



        }
        
        public bool Hitomi;
        public bool Sirome;


        public static List<string> Props_float = new List<string>
        {
            "_GlossinShadowonoff",
            "_SpeclarHeight",
            "_ShadowExtend",
            "_ReverseColor01",
            "_Color2onoff",
            "_Color3onoff",
            "_Cutoff",
            "_rimpower",
            "_rimV",
            "_oldhair",
            "_AlphaMaskuv",
            "_ReferenceAlphaMaskuv1",
            "_alpha_a",
            "_alpha_b",
            "_DetailBLineG",
            "_BackCullSwitch",
            "_ReferenceAlphaMaskuv",
            "_alpha",
            "_SpecularPower",
            "_SpecularPowerNail",
            "_ShadowExtendAnother",
            "_DetailRLineR",
            "_notusetexspecular",
            "_liquidftop",
            "_liquidfbot",
            "_liquidbtop",
            "_liquidbbot",
            "_liquidface",
            "_BumpScale",
            "_Float2",
            "_ColorSort",
            "_ColorInverse",
            "_Culloff",
            "_Float3",
            "_Matcap",
            "_MatcapSetting",
            "_FaceNormalG1",
            "_UPanner",
            "_VPanner",
            "_EmissionPower",
            "_EmissionBoost",
            "_LineWidthS",
            "_AnotherRampFull",
            "_AnotherRampMatcap",
            "_MultyColorAlpha",
            "_Dither",
            "_ColorMaskUse",
            "_isHighLight",
            "_exppower",
            "_rotation",
            "_rotation1",
            "_rotation2",
            "_rotation3",
            "_linetexon",
            "_tex1mask",
            "_nip",
            "_DetailNormalMapScale",
            "_nip_specular",
            "_nipsize",
            "_FaceNormalG",
            "_RimScale",

        };

        public static List<string> Props_float_G = new List<string>
        {
            "_KanoVerShift",
            "_linewidthG"
        };


        public static List<string> Props_Color = new List<string>
        {
            "_GlossColor",
            "_Color",
            "_ShadowColor",
            "_Color2",
            "_Color3",
            "_LineColor",
            "_SpecularColor",
            "_LiquidTiling",
            "_Color4",
            "_overcolor1",
            "_overcolor2",
            "_overcolor3",
            "_shadowcolor",
        };

        public static List<string> Props_Color_G = new List<string>
        {
            "_ambientshadowG",
            "_LightColor0",
            "_LineColorG"
        };


        public static List<string> Props_Tex = new List<string>
        {
            "_AnotherRamp",
            "_MainTex",
            "_NormalMap",
            "_AlphaMask",
            "_DetailMask",
            "_HairGloss",
            "_ColorMask",
            "_texcoord2",
            "_texcoord",
            "_LineMask",
            "_liquidmask",
            "_Texture2",
            "_GlassRamp",
            "_AnimationMask",
            "_overtex1",
            "_overtex2",
            "_overtex3",
            "_expression",
            "_texcoord3",
            "_NormalMapDetail",
            "_NormalMask",
            "_texcoord4",
        };
        public static List<string> Props_Tex_G = new List<string>
        {
            "_RampG",
        };

        static string CalculateMD5(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }


        public List<string> HashTable01 = new List<string>();
        public void WriteMatDummyData(Material Mat, string savepath, Renderer Rend)
        {
            
            Logger.LogMessage($"Attempting to Write {Mat.name}");
            //bool checkifdupe = false;
            savepath = savepath.Replace(".mat", "15TEMP15.mat");
            /*if (File.Exists(savepath))
            {
                savepath = savepath.Replace(".mat", "15TEMP15WHAT.mat");
                checkifdupe = true;
                Logger.LogMessage($"Material {Mat.name} Already Exists!\nLet me check if the properties are the same...");
            }*/
            var stream = File.CreateText(savepath);
            using(stream)
            {
                #region Write YAML .mat Header
                stream.WriteLine("%YAML 1.1");
                stream.WriteLine("%TAG !u! tag:unity3d.com,2011:");
                stream.WriteLine("--- !u!21 &2100000");
                stream.WriteLine("Material:");
                stream.WriteLine("  serializedVersion: 6");
                stream.WriteLine("  m_ObjectHideFlags: 0");
                stream.WriteLine("  m_CorrespondingSourceObject: {fileID: 0}");
                stream.WriteLine("  m_PrefabInstance: {fileID: 0}");
                stream.WriteLine("  m_PrefabAsset: {fileID: 0}");
                stream.WriteLine("  m_Name: Masked Material Exporter Material");
                stream.WriteLine("  m_Shader: {fileID: 4800000, guid: 90c15467d827c0a489063235abdf25e7, type: 3}");
                stream.WriteLine("  m_ShaderKeywords: _ALPHA_A_ON _ALPHA_B_ON _LINETEXON_ON _USELIGHTCOLORSPECULAR_ON");
                stream.WriteLine("    _USERAMPFORLIGHTS_ON");
                stream.WriteLine("  m_LightmapFlags: 4");
                stream.WriteLine("  m_EnableInstancingVariants: 0");
                stream.WriteLine("  m_DoubleSidedGI: 0");
                stream.WriteLine("  m_CustomRenderQueue: -1");
                stream.WriteLine("  stringTagMap: {}");
                stream.WriteLine("  disabledShaderPasses: []");
                stream.WriteLine("  m_SavedProperties:");
                stream.WriteLine("    serializedVersion: 3");
                stream.WriteLine("    m_TexEnvs:");
                #endregion

                #region Write Texture Blocks
                foreach (string s in Props_Tex)
                {
                    if (Mat.HasProperty(s))
                    {
                        stream.WriteLine($"    - {s}:");
                        stream.WriteLine("        m_Texture: {fileID: 0}");
                        stream.WriteLine($"        m_Scale: {{x: {Mat.GetTextureScale(s).x}, y: {Mat.GetTextureScale(s).y}}}");
                        stream.WriteLine($"        m_Offset: {{x: {Mat.GetTextureOffset(s).x}, y: {Mat.GetTextureOffset(s).y}}}");
                    }
                }
                #endregion
                #region Write Float Blocks
                stream.WriteLine("    m_Floats:");
                foreach(string s in Props_float)
                {
                    if(Mat.HasProperty(s))
                    {
                        stream.WriteLine($"    - {s}: {Mat.GetFloat(s)}");
                    }
                }
                foreach(string s in Props_float_G)
                {
                    stream.WriteLine($"    - {s}: {Shader.GetGlobalFloat(s)}");
                }
                #endregion
                #region Write Color/Vector4 Blocks
                stream.WriteLine("    m_Colors:");
                foreach(string s in Props_Color)
                {
                    if(Mat.HasProperty(s))
                    {
                        stream.WriteLine($"    - {s}: {{r: {Mat.GetColor(s).r}, g: {Mat.GetColor(s).g}, b: {Mat.GetColor(s).b}, a: {Mat.GetColor(s).a}}}");
                    }

                }
                foreach (string s in Props_Color_G)
                {
                    stream.WriteLine($"    - {s}: {{r: {Shader.GetGlobalColor(s).r}, g: {Shader.GetGlobalColor(s).g}, b: {Shader.GetGlobalColor(s).b}, a: {Shader.GetGlobalColor(s).a}}}");
                }
                #endregion




            }
            string hash = CalculateMD5(savepath);
            if(HashTable01.Count < 1)
            {
                HashTable01.Add(hash);
                Logger.LogMessage(hash);
                string newfilename = savepath.Replace("15TEMP15.mat", $"_{Rend.GetInstanceID()}.mat");
                File.Move(savepath, newfilename);
                return;


            }
            if (HashTable01 != null)
            {
                if(HashTable01.Contains(hash))
                {
                    File.Delete(savepath);
                    Logger.LogMessage($" Hashtable Already Contains {hash}, Discarding current material");
                    return;
                }
                else
                {
                    Logger.LogMessage(hash);
                    HashTable01.Add(hash);
                    string newfilename = savepath.Replace("15TEMP15.mat", $"_{Rend.GetInstanceID()}.mat");
                    File.Move(savepath, newfilename);
                }

            }

            

          
           /* if (checkifdupe)
            {
                string Original = File.ReadAllText(savepath.Replace("15TEMP15.mat", ".mat"));
                string New = File.ReadAllText(savepath);
                if(Original == New)
                {
                    
                    File.Delete(savepath);


                }
                else
                {
                    Logger.LogMessage($"Nope, This one is different, Saving with a different name");
                    int i = 0;
                    string newsavepath = savepath.Replace("15TEMP15.mat", ".mat");
                    newsavepath = newsavepath.Replace(".mat", $"{Mat.GetInstanceID()}.mat");
                    File.Move(savepath, newsavepath);

                }



            }*/
        }



        public void WriteMat1(Material Mat, string Path1)
        {
            string MatName = Mat.name;
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()) + "(){}[]$#@%^&*!;:<>\\/";
            
            foreach (char c in invalid)
            {
                MatName = MatName.Replace(c.ToString(), "");
            }
            string header = 
$@"%YAML 1.1
% TAG!u! tag: unity3d.com,2011:
---!u!21 & 2100000
Material:
        serializedVersion: 6
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{ fileID: 0}}
        m_PrefabInstance: {{ fileID: 0}}
        m_PrefabAsset: {{ fileID: 0}}
        m_Name: {MatName}
  m_Shader: {{ fileID: 4800000, guid: 90c15467d827c0a489063235abdf25e7, type: 3}}
        m_ShaderKeywords: _ALPHA_A_ON _ALPHA_B_ON _LINETEXON_ON _USELIGHTCOLORSPECULAR_ON
    _USERAMPFORLIGHTS_ON
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 0
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {{ }}
        disabledShaderPasses: []
        m_SavedProperties:
        serializedVersion: 3
    m_TexEnvs:
";





        }



        public Material materialToSave;
        //public string savePath = "SavedMaterial.mat";
        bool debug = false;
        /*public void SaveMaterial()
        {
            if (materialToSave != null)
            {
                // Create a new asset of type Material
                Material savedMaterial = new Material(materialToSave.shader);

                // Copy the properties of the original material to the new asset
                savedMaterial.CopyPropertiesFromMaterial(materialToSave);

                // Create a new instance of the MaterialPropertyBlock class
                MaterialPropertyBlock props = new MaterialPropertyBlock();

                // Apply the material to a temporary game object to capture the texture properties
                GameObject tempObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                tempObj.GetComponent<Renderer>().sharedMaterial = savedMaterial;

                // Get the texture properties from the temporary object
                tempObj.GetComponent<Renderer>().GetPropertyBlock(props);

                // Save the material to a file
                byte[] bytes = savedMaterial.SerializeToBytes();
                File.WriteAllBytes(savePath, bytes);

                // Destroy the temporary object
                Destroy(tempObj);
            }
        }*/
        public void Update()
        {
            #region Fix Materials


            if(Input.GetKeyDown(KeyCode.M) && Input.GetKey(KeyCode.LeftShift) && debug)
            {
                List<Material> Materials;
                Materials = new List<Material>();
                
                var Character = Resources.FindObjectsOfTypeAll<ChaControl>();
                foreach (ChaControl Charac in Character)
                {
                    CharacterName = Charac.fileParam.fullname;
                    break;

                }

                var Transforms = Resources.FindObjectsOfTypeAll<Transform>();
                foreach (Transform M in Transforms)
                {
                    if (M.gameObject.name == "chaF_001" || M.gameObject.name == "chaM_001")
                    {
                        //let hope this grabs both mesh and skinned mesh
                        foreach (Renderer Renderer in M.GetComponentsInChildren<Renderer>(true))
                        {
                            foreach(Material Mat in Renderer.sharedMaterials)
                            {
                                //time to check shaders

                                if(Mat.shader.name == "Custom/Blend/Additive")
                                {


                                }
                                if(Mat.shader.name == "Custom/Blend/Zero_Subtractive")
                                {


                                }





                            }


                        }
                    }
                }




                        }
            #endregion




            #region Export Materials
            if (ExportKeyBind.Value.IsDown())
            {
                HashTable01 = new List<string>();
                Properties = new string[14];
                Floats = new string[34];
                Colors = new string[8];
                List<string> P = new List<string>
                {
                    "_MainTex",
                    "_overtex1",
                    "_overtex2",
                    "_overtex3",
                    "_NormalMap",
                    "_NormalMapDetail",
                    "_DetailMask",
                    "_LineMask",
                    "_AlphaMask",
                    "_liquidmask",
                    "_Texture2",
                    "_Texture3",
                    "_NormalMask"
                };
                List<string> PV = new List<string>
                {
                    "_overcolor1",
                    "_overcolor2",
                    "_overcolor3",
                    "_EmissionColor",
                    "_ShadowColor",
                    "_SpecularColor",
                    "_LiquidTiling"


                };
                List<string> PF = new List<string>
                {
                    "_EmissionIntensity",
                    "_DetailNormalMapScale",
                    "_NormalMapScale",
                    "_SpeclarHeight",
                    "_SpecularPower",
                    "_SpecularPowerNail",
                    "_ShadowExtend",
                    "_rimpower",
                    "_rimV",
                    "_nipsize",
                    "_alpha_a",
                    "_alpha_b",
                    "_linetexon",
                    "_notusetexspecular",
                    "_nip",
                    "_nip_specular",
                    "_tex1mask",
                    "_LineWidthS"



                };
                
                var Character = Resources.FindObjectsOfTypeAll<ChaControl>();
                foreach(ChaControl Charac in Character)
                {
                    CharacterName = Charac.fileParam.fullname;
                    break;

                }
                
                
                System.IntPtr IntPtr3 = new IntPtr();
              
                
                System.IntPtr IntPtr2 = new IntPtr();
                
                
               var Meshs = Resources.FindObjectsOfTypeAll<Transform>();
                string InputDirMain = $"{Environment.CurrentDirectory}\\Materials\\Input\\";
                string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()) + "(){}[]$#@%^&*!;:<>\\/";
                /*if (!Directory.Exists($"{InputDirMain}{CharacterName}"))
                {
                    Directory.CreateDirectory($"{InputDirMain}{CharacterName}");

                }*/

                if (!Directory.Exists(ExportPath + "\\Materials\\" + CharacterName))
                {
                    Directory.CreateDirectory(ExportPath + "\\Materials\\" + CharacterName);

                }
                foreach (Transform M in Meshs)
                {
                    if(M.gameObject.name == "chaF_001" || M.gameObject.name == "chaM_001")
                    {
                        //let hope this grabs both mesh and skinned mesh
                        foreach(Renderer Mat in M.GetComponentsInChildren<Renderer>(true))
                        {
                            
                            foreach (Material MM in Mat.sharedMaterials)
                            {



                                

                              
                                string MatName = MM.name;
                                foreach (char c in invalid)
                                {
                                    MatName = MatName.Replace(c.ToString(), "");
                                }
                                //create file and then close it.



                                //new method!


                                //string InputDirChild = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                Logger.LogMessage($"{ExportPath}\\Materials\\{CharacterName}\\{MatName}.mat");
                                WriteMatDummyData(MM, $"{ExportPath}\\Materials\\{CharacterName}\\{MatName}.mat",Mat);
                                continue;
                                /*foreach(string file in Directory.GetFiles($"{InputDirMain}{CharacterName}\\"))
                                {
                                    Logger.LogMessage($"Searching for duplicates of {file}");
                                    foreach(string filesearch in Directory.GetFiles($"{InputDirMain}{CharacterName}\\"))
                                    {
                                        if (filesearch == file) continue;




                                    }




                                }*/

                                continue;

                                //let's just do all the materials

                                Logger.LogMessage(MM.shader.name);
                                //continue;

                                
                                #region eyes & eywhites
                                if (Mat.name == "cf_Ohitomi_L")
                                {

                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                    Logger.LogMessage($"Creating File {MatName} => Template EyeWhite Assigned Left.mat");
                                    string Text = File.ReadAllText($"{InputDir}EyeW.mat");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}", $"    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                    File.WriteAllText($"{InputDirMain}{CharacterName}\\EyeWhite Assigned Left.mat", Text);
                                    continue;





                                }
                                if (Mat.name == "cf_Ohitomi_R")
                                {

                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                    Logger.LogMessage($"Creating File {MatName} => Template EyeWhite Assigned Right.mat");
                                    string Text = File.ReadAllText($"{InputDir}EyeW.mat");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}", $"    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                    File.WriteAllText($"{InputDirMain}{CharacterName}\\EyeWhite Assigned Right.mat", Text);
                                    continue;





                                }
                                if (Mat.name == "cf_Ohitomi_L02")
                                {

                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                    Logger.LogMessage($"Creating File {MatName} => Template Eye Assigned Left.mat");
                                    string Text = File.ReadAllText($"{InputDir}Eye.mat");
                                    Text = Text.Replace($"    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _expression:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _overtex1:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _overtex2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}", $"    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_MainTex").x}, y: {MM.GetTextureScale("_MainTex").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_MainTex").x}, y: {MM.GetTextureOffset("_MainTex").y}}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _expression:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_expression").x}, y: {MM.GetTextureScale("_expression").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_expression").x}, y: {MM.GetTextureOffset("_expression").y}}}\n    - _overtex1:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_overtex1").x}, y: {MM.GetTextureScale("_overtex1").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_overtex1").x}, y: {MM.GetTextureOffset("_overtex1").y}}}\n    - _overtex2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_overtex2").x}, y: {MM.GetTextureScale("_overtex2").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_overtex2").x}, y: {MM.GetTextureOffset("_overtex2").y}}}");
                                    Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _ExpressionDepth: 1\n    - _ExpressionSize: 0.35\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _SmoothnessTextureChannel: 0\n    - _SpecularHighlights: 1\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseRampForLights: 1\n    - _ZWrite: 1\n    - _exppower: 1\n    - _isHighLight: 0\n    - _rotation: 0", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _ExpressionDepth: {MM.GetFloat("_ExpressionDepth")}\n    - _ExpressionSize: {MM.GetFloat("_ExpressionSize")}\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _SmoothnessTextureChannel: 0\n    - _SpecularHighlights: 1\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseRampForLights: 1\n    - _ZWrite: 1\n    - _exppower: {MM.GetFloat("_exppower")}\n    - _isHighLight: {MM.GetFloat("_isHighLight")}\n    - _rotation: {MM.GetFloat("_rotation")}");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}", $"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _overcolor1: {{r: {MM.GetVector("_overcolor1").x}, g: {MM.GetVector("_overcolor1").y}, b: {MM.GetVector("_overcolor1").z}, a: {MM.GetVector("_overcolor1").w}}}\n    - _overcolor2: {{r: {MM.GetVector("_overcolor2").x}, g: {MM.GetVector("_overcolor2").y}, b: {MM.GetVector("_overcolor2").z}, a: {MM.GetVector("_overcolor2").w}}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                    File.WriteAllText($"{InputDirMain}{CharacterName}\\Eye Assigned Left.mat", Text);
                                    continue;


                                }
                                if (Mat.name == "cf_Ohitomi_R02")
                                {

                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                    Logger.LogMessage($"Creating File {MatName} => Template Eye Assigned Right.mat");
                                    string Text = File.ReadAllText($"{InputDir}Eye.mat");
                                    Text = Text.Replace($"    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _expression:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _overtex1:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _overtex2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}", $"    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_MainTex").x}, y: {MM.GetTextureScale("_MainTex").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_MainTex").x}, y: {MM.GetTextureOffset("_MainTex").y}}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _expression:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_expression").x}, y: {MM.GetTextureScale("_expression").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_expression").x}, y: {MM.GetTextureOffset("_expression").y}}}\n    - _overtex1:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_overtex1").x}, y: {MM.GetTextureScale("_overtex1").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_overtex1").x}, y: {MM.GetTextureOffset("_overtex1").y}}}\n    - _overtex2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_overtex2").x}, y: {MM.GetTextureScale("_overtex2").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_overtex2").x}, y: {MM.GetTextureOffset("_overtex2").y}}}");
                                    Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _ExpressionDepth: 1\n    - _ExpressionSize: 0.35\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _SmoothnessTextureChannel: 0\n    - _SpecularHighlights: 1\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseRampForLights: 1\n    - _ZWrite: 1\n    - _exppower: 1\n    - _isHighLight: 0\n    - _rotation: 0", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _ExpressionDepth: {MM.GetFloat("_ExpressionDepth")}\n    - _ExpressionSize: {MM.GetFloat("_ExpressionSize")}\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _SmoothnessTextureChannel: 0\n    - _SpecularHighlights: 1\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseRampForLights: 1\n    - _ZWrite: 1\n    - _exppower: {MM.GetFloat("_exppower")}\n    - _isHighLight: {MM.GetFloat("_isHighLight")}\n    - _rotation: {MM.GetFloat("_rotation")}");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}", $"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _overcolor1: {{r: {MM.GetVector("_overcolor1").x}, g: {MM.GetVector("_overcolor1").y}, b: {MM.GetVector("_overcolor1").z}, a: {MM.GetVector("_overcolor1").w}}}\n    - _overcolor2: {{r: {MM.GetVector("_overcolor2").x}, g: {MM.GetVector("_overcolor2").y}, b: {MM.GetVector("_overcolor2").z}, a: {MM.GetVector("_overcolor2").w}}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                    File.WriteAllText($"{InputDirMain}{CharacterName}\\Eye Assigned Right.mat", Text);
                                    continue;


                                }
                                #endregion

                                //skin

                                
                                if(MM.shader.name == "Shader Forge/main_skin")
                                {
                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                    Logger.LogMessage($"Creating File {MatName} => {MatName} Assigned.mat");
                                    string Text = File.ReadAllText($"{InputDir}Skin.mat");
                                    Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _LineWidthS: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _NormalMapScale: 1\n    - _OcclusionStrength: 1\n    - _OutlineOn: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: 1\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SpecularPowerNail: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseDetailRAsSpecularMap: 0\n    - _UseLightColorSpecular: 1\n    - _UseRampForLights: 1\n    - _UseRampForSpecular: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: 1\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: 0\n    - _nip_specular: 0.5\n    - _nipsize: 0.5\n    - _notusetexspecular: 0\n    - _rimV: 0\n    - _rimpower: 0.5\n    - _tex1mask: 0", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _GlossMapScale: 1\n    - _Glossiness: 0\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: 1\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SpecularPowerNail: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: 1\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: 0\n    - _nip_specular: 0.5\n    - _nipsize: 0.5\n    - _notusetexspecular: 0\n    - _rimV: 0\n    - _rimpower: 0.5\n    - _tex1mask: 0");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: 0, g: 0, b: 2, a: 2}}\n    - _OutlineColor: {{r: 0, g: 0, b: 0, a: 0}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor3: {{r: 1, g: 1, b: 1, a: 1}}", $"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: 0, g: 0, b: 2, a: 2}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor3: {{r: 1, g: 1, b: 1, a: 1}}");
                                    Text = Text.Replace($"    - _Texture2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _Texture3:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}", $"    - _Texture2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_Texture2").x}, y: {MM.GetTextureScale("_Texture2").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_Texture2").x}, y: {MM.GetTextureOffset("_Texture2").y}}}\n    - _Texture3:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_Texture3").x}, y: {MM.GetTextureScale("_Texture3").y}}}\n        m_Offset: {{x: 0, y: 0}}");
                                    Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _GlossMapScale: 1\n    - _Glossiness: 0\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: 1\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SpecularPowerNail: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: 1\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: 0\n    - _nip_specular: 0.5\n    - _nipsize: 0.5\n    - _notusetexspecular: 0\n    - _rimV: 0\n    - _rimpower: 0.5\n    - _tex1mask: 0", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: {MM.GetFloat("_DetailNormalMapScale")}\n    - _DstBlend: 0\n    - _GlossMapScale: 1\n    - _Glossiness: 0\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: {MM.GetFloat("_ShadowExtend")}\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: {MM.GetFloat("_SpecularHeight")}\n    - _SpecularHighlights: 1\n    - _SpecularPower: {MM.GetFloat("_SpecularPower")}\n    - _SpecularPowerNail: {MM.GetFloat("_SpecularPowerNail")}\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: {MM.GetFloat("_linetexon")}\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: {MM.GetFloat("_nip")}\n    - _nip_specular: {MM.GetFloat("_nip_specular")}\n    - _nipsize: {MM.GetFloat("_nipsize")}\n    - _notusetexspecular: {MM.GetFloat("_notusetexspecular")}\n    - _rimV: {MM.GetFloat("_rimV")}\n    - _rimpower: 0.5\n    - _tex1mask: {MM.GetFloat("_tex1mask")}");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: 0, g: 0, b: 2, a: 2}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor3: {{r: 1, g: 1, b: 1, a: 1}}", $"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: {MM.GetVector("_LiquidTiling").x}, g: {MM.GetVector("_LiquidTiling").y}, b: {MM.GetVector("_LiquidTiling").z}, a: {MM.GetVector("_LiquidTiling").w}}}\n    - _ShadowColor: {{r: {MM.GetVector("_ShadowColor").x}, g: {MM.GetVector("_ShadowColor").y}, b: {MM.GetVector("_ShadowColor").z}, a: {MM.GetVector("_ShadowColor").w}}}\n    - _SpecularColor: {{r: {MM.GetVector("_SpecularColor").x}, g: {MM.GetVector("_SpecularColor").y}, b: {MM.GetVector("_SpecularColor").z}, a: {MM.GetVector("_SpecularColor").w}}}\n    - _overcolor1: {{r: {MM.GetVector("_overcolor1").x}, g: {MM.GetVector("_overcolor1").y}, b: {MM.GetVector("_overcolor1").z}, a: {MM.GetVector("_overcolor1").w}}}\n    - _overcolor2: {{r: {MM.GetVector("_overcolor2").x}, g: {MM.GetVector("_overcolor2").y}, b: {MM.GetVector("_overcolor2").z}, a: {MM.GetVector("_overcolor2").w}}}\n    - _overcolor3: {{r: {MM.GetVector("_overcolor3").x}, g: {MM.GetVector("_overcolor3").y}, b: {MM.GetVector("_overcolor3").z}, a: {MM.GetVector("_overcolor3").w}}}");
                                    File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat", Text);
                                    continue;

                                }
                                //other things that use eyeWhite shader
                                if(MM.shader.name == "Shader Forge/toon_nose_lod0" || MM.shader.name == "Shader Forge/toon_eyew_lod0")
                                {
                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                    Logger.LogMessage($"Creating File {MatName} => {MatName} Assigned.mat");
                                    string Text = File.ReadAllText($"{InputDir}EyeW.mat");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}", $"    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                    File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat", Text);
                                    continue;

                                }
                                //hair
                                if(MM.shader.name == "Shader Forge/main_hair")
                                {
                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                    Logger.LogMessage($"Creating File {MatName} => {MatName} Assigned.mat");
                                    string Text = File.ReadAllText($"{InputDir}Hair.mat");
                                    //floats
                                    Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _LineWidthS: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _NormalMapScale: 1\n    - _OcclusionStrength: 1\n    - _OutlineOn: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: 0.5\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.85\n    - _SpecularHairPower: 1\n    - _SpecularHeightInvert: 0\n    - _SpecularHighlights: 1\n    - _SpecularIsHighLightsPow: 64\n    - _SpecularIsHighlights: 0\n    - _SpecularIsHighlightsRange: 5\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseDetailRAsSpecularMap: 0\n    - _UseLightColorSpecular: 1\n    - _UseMeshSpecular: 0\n    - _UseRampForLights: 1\n    - _UseRampForSpecular: 0\n    - _ZWrite: 1\n    - _rimV: 0.75\n    - _rimpower: 0.5", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _LineWidthS: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _NormalMapScale: 1\n    - _OcclusionStrength: 1\n    - _OutlineOn: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: {MM.GetFloat("_ShadowExtend")}\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: {MM.GetFloat("_SpecularHeight")}\n    - _SpecularHairPower: 1\n    - _SpecularHeightInvert: 0\n    - _SpecularHighlights: 1\n    - _SpecularIsHighLightsPow: 64\n    - _SpecularIsHighlights: 0\n    - _SpecularIsHighlightsRange: 5\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseDetailRAsSpecularMap: 0\n    - _UseLightColorSpecular: 1\n    - _UseMeshSpecular: 0\n    - _UseRampForLights: 1\n    - _UseRampForSpecular: 0\n    - _ZWrite: 1\n    - _rimV: {MM.GetFloat("_rimV")}\n    - _rimpower: {MM.GetFloat("_rimpower")}");
                                    //colors (vector4)
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _Color2: {{r: 0.7843137, g: 0.7843137, b: 0.7843137, a: 1}}\n    - _Color3: {{r: 0.5, g: 0.5, b: 0.5, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _GlossColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _LineColor: {{r: 0.5, g: 0.5, b: 0.5, a: 1}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}", $"    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _Color2: {{r: {MM.GetVector("_Color2").x}, g: {MM.GetVector("_Color2").y}, b: {MM.GetVector("_Color2").z}, a: {MM.GetVector("_Color2").w}}}\n    - _Color3: {{r: {MM.GetVector("_Color3").x}, g: {MM.GetVector("_Colo3r").y}, b: {MM.GetVector("_Color3").z}, a: {MM.GetVector("_Color3").w}}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _GlossColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _LineColor: {{r: {MM.GetVector("_LineColor").x}, g: {MM.GetVector("_LineColor").y}, b: {MM.GetVector("_LineColor").z}, a: {MM.GetVector("_LineColor").w}}}\n    - _ShadowColor: {{r: {MM.GetVector("_ShadowColor").x}, g: {MM.GetVector("_ShadowColor").y}, b: {MM.GetVector("_ShadowColor").z}, a: {MM.GetVector("_ShadowColor").w}}}\n    - _SpecularColor: {{r: {MM.GetVector("_SpecularColor").x}, g: {MM.GetVector("_SpecularColor").y}, b: {MM.GetVector("_SpecularColor").z}, a: {MM.GetVector("_SpecularColor").w}}}");


                                    if(File.Exists(($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat")))
                                    {
                                        Logger.LogMessage($"{MatName} Already Exists! Checking if we should skip or make a new one");
                                        if (Text == File.ReadAllText(($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat")))
                                        {
                                            Logger.LogMessage($"{MatName} Has the same data as the material we are checking, Skipping!");
                                            continue;

                                        }
                                        Logger.LogMessage($"{MatName} Does not have the same data as the material we are checking, Writing a new one!");
                                        File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName}_{MM.GetInstanceID()} Assigned.mat", Text);
                                        continue;
                                    }
                                    //first instance
                                    File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat", Text);
                                    //no need for texture offset/scale. at least I'm pretty sure. I could be wrong
                                    //ok well i know why it only found some hair and not all, the rest of the hair is not skinned
                                    continue;
                                }
                                //hair front can just use the same values, the props are the same but the actual shader code might be diff idk
                                if (MM.shader.name == "Shader Forge/main_hair_front")
                                {
                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                    Logger.LogMessage($"Creating File {MatName} => {MatName} Assigned.mat");
                                    string Text = File.ReadAllText($"{InputDir}HairFront.mat");
                                    //floats
                                    Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _LineWidthS: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _NormalMapScale: 1\n    - _OcclusionStrength: 1\n    - _OutlineOn: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: 0.5\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.85\n    - _SpecularHairPower: 1\n    - _SpecularHeightInvert: 0\n    - _SpecularHighlights: 1\n    - _SpecularIsHighLightsPow: 64\n    - _SpecularIsHighlights: 0\n    - _SpecularIsHighlightsRange: 5\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseDetailRAsSpecularMap: 0\n    - _UseLightColorSpecular: 1\n    - _UseMeshSpecular: 0\n    - _UseRampForLights: 1\n    - _UseRampForSpecular: 0\n    - _ZWrite: 1\n    - _rimV: 0.75\n    - _rimpower: 0.5", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _LineWidthS: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _NormalMapScale: 1\n    - _OcclusionStrength: 1\n    - _OutlineOn: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: {MM.GetFloat("_ShadowExtend")}\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: {MM.GetFloat("_SpecularHeight")}\n    - _SpecularHairPower: 1\n    - _SpecularHeightInvert: 0\n    - _SpecularHighlights: 1\n    - _SpecularIsHighLightsPow: 64\n    - _SpecularIsHighlights: 0\n    - _SpecularIsHighlightsRange: 5\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseDetailRAsSpecularMap: 0\n    - _UseLightColorSpecular: 1\n    - _UseMeshSpecular: 0\n    - _UseRampForLights: 1\n    - _UseRampForSpecular: 0\n    - _ZWrite: 1\n    - _rimV: {MM.GetFloat("_rimV")}\n    - _rimpower: {MM.GetFloat("_rimpower")}");
                                    //colors (vector4)
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _Color2: {{r: 0.7843137, g: 0.7843137, b: 0.7843137, a: 1}}\n    - _Color3: {{r: 0.5, g: 0.5, b: 0.5, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _GlossColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _LineColor: {{r: 0.5, g: 0.5, b: 0.5, a: 1}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}", $"    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _Color2: {{r: {MM.GetVector("_Color2").x}, g: {MM.GetVector("_Color2").y}, b: {MM.GetVector("_Color2").z}, a: {MM.GetVector("_Color2").w}}}\n    - _Color3: {{r: {MM.GetVector("_Color3").x}, g: {MM.GetVector("_Colo3r").y}, b: {MM.GetVector("_Color3").z}, a: {MM.GetVector("_Color3").w}}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _GlossColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _LineColor: {{r: {MM.GetVector("_LineColor").x}, g: {MM.GetVector("_LineColor").y}, b: {MM.GetVector("_LineColor").z}, a: {MM.GetVector("_LineColor").w}}}\n    - _ShadowColor: {{r: {MM.GetVector("_ShadowColor").x}, g: {MM.GetVector("_ShadowColor").y}, b: {MM.GetVector("_ShadowColor").z}, a: {MM.GetVector("_ShadowColor").w}}}\n    - _SpecularColor: {{r: {MM.GetVector("_SpecularColor").x}, g: {MM.GetVector("_SpecularColor").y}, b: {MM.GetVector("_SpecularColor").z}, a: {MM.GetVector("_SpecularColor").w}}}");
                                    if (File.Exists(($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat")))
                                    {
                                        Logger.LogMessage($"{MatName} Already Exists! Checking if we should skip or make a new one");
                                        if (Text == File.ReadAllText(($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat")))
                                        {
                                            Logger.LogMessage($"{MatName} Has the same data as the material we are checking, Skipping!");
                                            continue;

                                        }
                                        Logger.LogMessage($"{MatName} Does not have the same data as the material we are checking, Writing a new one!");
                                        File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName}_{MM.GetInstanceID()} Assigned.mat", Text);
                                        continue;
                                    }


                                    //first instance
                                    File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat", Text);
                                    //no need for texture offset/scale. at least I'm pretty sure. I could be wrong
                                    continue;
                                    //actually since this is hair we should do a double check to see if the values are the same and if they arent create a new file

                                }




                                //item
                                if(MM.shader.name == "Shader Forge/main_item")
                                {
                                        string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                        Logger.LogMessage($"Creating File {MatName} => {MatName} Assigned.mat");
                                        string Text = File.ReadAllText($"{InputDir}Item.mat");
                                    //scale and offsets
                                    Text = Text.Replace($"    - _AnotherRamp:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _BumpMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ColorMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _DetailAlbedoMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _DetailMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _DetailNormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _EmissionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _EmissionMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _LineMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _NormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _NormalMapDetail:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ReflectionMapCap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}", $"    - _AnotherRamp:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_AnotherRamp").x}, y: {MM.GetTextureScale("_AnotherRamp").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_AnotherRamp").x}, y: {MM.GetTextureOffset("_AnotherRamp").y}}}\n    - _BumpMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_BumpMap").x}, y: {MM.GetTextureScale("_BumpMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_BumpMap").x}, y: {MM.GetTextureOffset("_BumpMap").y}}}\n    - _ColorMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_ColorMask").x}, y: {MM.GetTextureScale("_ColorMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_ColorMask").x}, y: {MM.GetTextureOffset("_ColorMask").y}}}\n    - _DetailAlbedoMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_DetailAlbedoMap").x}, y: {MM.GetTextureScale("_DetailAlbedoMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_DetailAlbedoMap").x}, y: {MM.GetTextureOffset("_DetailAlbedoMap").y}}}\n    - _DetailMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_DetailMask").x}, y: {MM.GetTextureScale("_DetailMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_DetailMask").x}, y: {MM.GetTextureOffset("_DetailMask").y}}}\n    - _DetailNormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_DetailNormalMap").x}, y: {MM.GetTextureScale("_DetailNormalMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_DetailNormalMap").x}, y: {MM.GetTextureOffset("_DetailNormalMap").y}}}\n    - _EmissionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_EmissionMap").x}, y: {MM.GetTextureScale("_EmissionMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_EmissionMap").x}, y: {MM.GetTextureOffset("_EmissionMap").y}}}\n    - _EmissionMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_EmissionMask").x}, y: {MM.GetTextureScale("_EmissionMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_EmissionMask").x}, y: {MM.GetTextureOffset("_EmissionMask").y}}}\n    - _LineMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_LineMask").x}, y: {MM.GetTextureScale("_LineMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_LineMask").x}, y: {MM.GetTextureOffset("_LineMask").y}}}\n    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_MainTex").x}, y: {MM.GetTextureScale("_MainTex").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_MainTex").x}, y: {MM.GetTextureOffset("_MainTex").y}}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_MetallicGlossMap").x}, y: {MM.GetTextureScale("_MetallicGlossMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_MetallicGlossMap").x}, y: {MM.GetTextureOffset("_MetallicGlossMap").y}}}\n    - _NormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_NormalMap").x}, y: {MM.GetTextureScale("_NormalMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_NormalMap").x}, y: {MM.GetTextureOffset("_NormalMap").y}}}\n    - _NormalMapDetail:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_NormalMapDetail").x}, y: {MM.GetTextureScale("_NormalMapDetail").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_NormalMapDetail").x}, y: {MM.GetTextureOffset("_NormalMapDetail").y}}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_OcclusionMap").x}, y: {MM.GetTextureScale("_OcclusionMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_OcclusionMap").x}, y: {MM.GetTextureOffset("_OcclusionMap").y}}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_ParallaxMap").x}, y: {MM.GetTextureScale("_ParallaxMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_ParallaxMap").x}, y: {MM.GetTextureOffset("_ParallaxMap").y}}}\n    - _ReflectionMapCap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_ReflectionMapCap").x}, y: {MM.GetTextureScale("_ReflectionMapCap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_ReflectionMapCap").x}, y: {MM.GetTextureOffset("_ReflectionMapCap").y}}}");
                                    //floats
                                    Text = Text.Replace($"    - _AlphaOptionCutoff: 1\n    - _AnotherRampFull: 0\n    - _BumpScale: 1\n    - _CullOption: 0\n    - _Cutoff: 0.5\n    - _DetailBLineG: 0\n    - _DetailNormalMapScale: 1\n    - _DetailRLineR: 0\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _LineWidthS: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _NormalMapScale: 1\n    - _OcclusionStrength: 1\n    - _OutlineOn: 1\n    - _Parallax: 0.02\n    - _Reflective: 0.75\n    - _ReflectiveBlend: 0.05\n    - _ReflectiveMulOrAdd: 1\n    - _ShadowExtend: 1\n    - _ShadowExtendAnother: 1\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseDetailRAsSpecularMap: 0\n    - _UseKKMetal: 1\n    - _UseLightColorSpecular: 1\n    - _UseMatCapReflection: 1\n    - _UseRampForLights: 1\n    - _UseRampForSpecular: 0\n    - _ZWrite: 1\n    - _notusetexspecular: 0\n    - _rimV: 0.5\n    - _rimpower: 0.5", $"    - _AlphaOptionCutoff: {MM.GetFloat("_AlphaOptionCutoff")}\n    - _AnotherRampFull: {MM.GetFloat("_AnotherRampFull")}\n    - _BumpScale: {MM.GetFloat("_BumpScale")}\n    - _CullOption: {MM.GetFloat("_CullOption")}\n    - _Cutoff: {MM.GetFloat("_Cutoff")}\n    - _DetailBLineG: {MM.GetFloat("_DetailBLineG")}\n    - _DetailNormalMapScale: {MM.GetFloat("_DetailNormalMapScale")}\n    - _DetailRLineR: {MM.GetFloat("_DetailRLineR")}\n    - _DstBlend: {MM.GetFloat("_DstBlend")}\n    - _EmissionIntensity: {MM.GetFloat("_EmissionIntensity")}\n    - _GlossMapScale: {MM.GetFloat("_GlossMapScale")}\n    - _Glossiness: {MM.GetFloat("_Glossiness")}\n    - _GlossyReflections: {MM.GetFloat("_GlossyReflections")}\n    - _LineWidthS: {MM.GetFloat("_LineWidthS")}\n    - _Metallic: {MM.GetFloat("_Metallic")}\n    - _Mode: {MM.GetFloat("_Mode")}\n    - _NormalMapScale: {MM.GetFloat("_NormalMapScale")}\n    - _OcclusionStrength: {MM.GetFloat("_OcclusionStrength")}\n    - _OutlineOn: {MM.GetFloat("_OutlineOn")}\n    - _Parallax: {MM.GetFloat("_Parallax")}\n    - _Reflective: {MM.GetFloat("_Reflective")}\n    - _ReflectiveBlend: {MM.GetFloat("_ReflectiveBlend")}\n    - _ReflectiveMulOrAdd: {MM.GetFloat("_ReflectiveMulOrAdd")}\n    - _ShadowExtend: {MM.GetFloat("_ShadowExtend")}\n    - _ShadowExtendAnother: {MM.GetFloat("_ShadowExtendAnother")}\n    - _SmoothnessTextureChannel: {MM.GetFloat("_SmoothnessTextureChannel")}\n    - _SpeclarHeight: {MM.GetFloat("_SpeclarHeight")}\n    - _SpecularHighlights: {MM.GetFloat("_SpecularHighlights")}\n    - _SpecularPower: {MM.GetFloat("_SpecularPower")}\n    - _SrcBlend: {MM.GetFloat("_SrcBlend")}\n    - _UVSec: {MM.GetFloat("_UVSec")}\n    - _UseDetailRAsSpecularMap: {MM.GetFloat("_UseDetailRAsSpecularMap")}\n    - _UseKKMetal: {MM.GetFloat("_UseKKMetal")}\n    - _UseLightColorSpecular: {MM.GetFloat("_UseLightColorSpecular")}\n    - _UseMatCapReflection: {MM.GetFloat("_UseMatCapReflection")}\n    - _UseRampForLights: {MM.GetFloat("_UseRampForLights")}\n    - _UseRampForSpecular: {MM.GetFloat("_UseRampForSpecular")}\n    - _ZWrite: {MM.GetFloat("_ZWrite")}\n    - _notusetexspecular: {MM.GetFloat("_notusetexspecular")}\n    - _rimV: {MM.GetFloat("_rimV")}\n    - _rimpower: {MM.GetFloat("_rimpower")}");
                                    //colors(vector4)
                                    Text = Text.Replace($"    - _Clock: {{r: 0, g: 0, b: 0, a: 0}}\n    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _Color2: {{r: 0.11724187, g: 0, b: 1, a: 1}}\n    - _Color3: {{r: 0.5, g: 0.5, b: 0.5, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _OutlineColor: {{r: 0, g: 0, b: 0, a: 0}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}", $"    - _Clock: {{r: {MM.GetVector("_Clock").x}, g: {MM.GetVector("_Clock").y}, b: {MM.GetVector("_Clock").z}, a: {MM.GetVector("_Clock").w}}}\n    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _Color2: {{r: {MM.GetVector("_Color2").x}, g: {MM.GetVector("_Color2").y}, b: {MM.GetVector("_Color2").z}, a: {MM.GetVector("_Color2").w}}}\n    - _Color3: {{r: {MM.GetVector("_Color3").x}, g: {MM.GetVector("_Color3").y}, b: {MM.GetVector("_Color3").z}, a: {MM.GetVector("_Color3").w}}}\n    - _CustomAmbient: {{r: {MM.GetVector("_CustomAmbient").x}, g: {MM.GetVector("_CustomAmbient").y}, b: {MM.GetVector("_CustomAmbient").z}, a: {MM.GetVector("_CustomAmbient").w}}}\n    - _EmissionColor: {{r: {MM.GetVector("_EmissionColor").x}, g: {MM.GetVector("_EmissionColor").y}, b: {MM.GetVector("_EmissionColor").z}, a: {MM.GetVector("_EmissionColor").w}}}\n    - _OutlineColor: {{r: {MM.GetVector("_OutlineColor").x}, g: {MM.GetVector("_OutlineColor").y}, b: {MM.GetVector("_OutlineColor").z}, a: {MM.GetVector("_OutlineColor").w}}}\n    - _ShadowColor: {{r: {MM.GetVector("_ShadowColor").x}, g: {MM.GetVector("_ShadowColor").y}, b: {MM.GetVector("_ShadowColor").z}, a: {MM.GetVector("_ShadowColor").w}}}\n    - _SpecularColor: {{r: {MM.GetVector("_SpecularColor").x}, g: {MM.GetVector("_SpecularColor").y}, b: {MM.GetVector("_SpecularColor").z}, a: {MM.GetVector("_SpecularColor").w}}}");

                                    if (File.Exists(($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat")))
                                    {
                                        Logger.LogMessage($"{MatName} Already Exists! Checking if we should skip or make a new one");
                                        if (Text == File.ReadAllText(($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat")))
                                        {
                                            Logger.LogMessage($"{MatName} Has the same data as the material we are checking, Skipping!");
                                            continue;

                                        }
                                        Logger.LogMessage($"{MatName} Does not have the same data as the material we are checking, Writing a new one!");
                                        File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName}_{MM.GetInstanceID()} Assigned.mat", Text);
                                        continue;
                                    }
                                    //first instance
                                    File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat", Text);
                                    continue;
                                    //since we are using all the values instead of the ones that only appear in the real shader, we wull get alot of erros about a property not existing in the material, but its fine I think.
                                }

                                if (MM.shader.name == "Shader Forge/main_alpha")
                                {
                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                    Logger.LogMessage($"Creating File {MatName} => {MatName} Assigned.mat");
                                    string Text = File.ReadAllText($"{InputDir}ItemAlpha.mat");
                                    //scale and offsets
                                    Text = Text.Replace($"    - _AnotherRamp:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _BumpMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ColorMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _DetailAlbedoMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _DetailMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _DetailNormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _EmissionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _EmissionMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _LineMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _NormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _NormalMapDetail:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ReflectionMapCap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}",$"    - _AnotherRamp:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_AnotherRamp").x}, y: {MM.GetTextureScale("_AnotherRamp").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_AnotherRamp").x}, y: {MM.GetTextureOffset("_AnotherRamp").y}}}\n    - _BumpMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_BumpMap").x}, y: {MM.GetTextureScale("_BumpMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_BumpMap").x}, y: {MM.GetTextureOffset("_BumpMap").y}}}\n    - _ColorMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_ColorMask").x}, y: {MM.GetTextureScale("_ColorMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_ColorMask").x}, y: {MM.GetTextureOffset("_ColorMask").y}}}\n    - _DetailAlbedoMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_DetailAlbedoMap").x}, y: {MM.GetTextureScale("_DetailAlbedoMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_DetailAlbedoMap").x}, y: {MM.GetTextureOffset("_DetailAlbedoMap").y}}}\n    - _DetailMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_DetailMask").x}, y: {MM.GetTextureScale("_DetailMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_DetailMask").x}, y: {MM.GetTextureOffset("_DetailMask").y}}}\n    - _DetailNormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_DetailNormalMap").x}, y: {MM.GetTextureScale("_DetailNormalMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_DetailNormalMap").x}, y: {MM.GetTextureOffset("_DetailNormalMap").y}}}\n    - _EmissionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_EmissionMap").x}, y: {MM.GetTextureScale("_EmissionMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_EmissionMap").x}, y: {MM.GetTextureOffset("_EmissionMap").y}}}\n    - _EmissionMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_EmissionMask").x}, y: {MM.GetTextureScale("_EmissionMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_EmissionMask").x}, y: {MM.GetTextureOffset("_EmissionMask").y}}}\n    - _LineMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_LineMask").x}, y: {MM.GetTextureScale("_LineMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_LineMask").x}, y: {MM.GetTextureOffset("_LineMask").y}}}\n    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_MainTex").x}, y: {MM.GetTextureScale("_MainTex").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_MainTex").x}, y: {MM.GetTextureOffset("_MainTex").y}}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_MetallicGlossMap").x}, y: {MM.GetTextureScale("_MetallicGlossMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_MetallicGlossMap").x}, y: {MM.GetTextureOffset("_MetallicGlossMap").y}}}\n    - _NormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_NormalMap").x}, y: {MM.GetTextureScale("_NormalMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_NormalMap").x}, y: {MM.GetTextureOffset("_NormalMap").y}}}\n    - _NormalMapDetail:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_NormalMapDetail").x}, y: {MM.GetTextureScale("_NormalMapDetail").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_NormalMapDetail").x}, y: {MM.GetTextureOffset("_NormalMapDetail").y}}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_OcclusionMap").x}, y: {MM.GetTextureScale("_OcclusionMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_OcclusionMap").x}, y: {MM.GetTextureOffset("_OcclusionMap").y}}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_ParallaxMap").x}, y: {MM.GetTextureScale("_ParallaxMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_ParallaxMap").x}, y: {MM.GetTextureOffset("_ParallaxMap").y}}}\n    - _ReflectionMapCap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_ReflectionMapCap").x}, y: {MM.GetTextureScale("_ReflectionMapCap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_ReflectionMapCap").x}, y: {MM.GetTextureOffset("_ReflectionMapCap").y}}}");
                                    //floats
                                    Text = Text.Replace($"    - _Alpha: 1\n    - _AlphaOptionCutoff: 1\n    - _AlphaOptionZWrite: 1\n    - _AnotherRampFull: 0\n    - _BumpScale: 1\n    - _CullOption: 2\n    - _Cutoff: 0.5\n    - _DetailBLineG: 0\n    - _DetailNormalMapScale: 1\n    - _DetailRLineR: 0\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _LineWidthS: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _NormalMapScale: 1\n    - _OcclusionStrength: 1\n    - _OutlineOn: 0\n    - _Parallax: 0.02\n    - _Reflective: 0.75\n    - _ReflectiveBlend: 0.05\n    - _ReflectiveMulOrAdd: 1\n    - _ShadowExtend: 1\n    - _ShadowExtendAnother: 1\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseDetailRAsSpecularMap: 0\n    - _UseKKMetal: 1\n    - _UseLightColorSpecular: 1\n    - _UseMatCapReflection: 1\n    - _UseRampForLights: 1\n    - _UseRampForSpecular: 0\n    - _ZWrite: 1\n    - _notusetexspecular: 0\n    - _rimV: 0.5\n    - _rimpower: 0.5",$"    - _AlphaOptionCutoff: {MM.GetFloat("_AlphaOptionCutoff")}\n    - _AnotherRampFull: {MM.GetFloat("_AnotherRampFull")}\n    - _BumpScale: {MM.GetFloat("_BumpScale")}\n    - _CullOption: {MM.GetFloat("_CullOption")}\n    - _Cutoff: {MM.GetFloat("_Cutoff")}\n    - _DetailBLineG: {MM.GetFloat("_DetailBLineG")}\n    - _DetailNormalMapScale: {MM.GetFloat("_DetailNormalMapScale")}\n    - _DetailRLineR: {MM.GetFloat("_DetailRLineR")}\n    - _DstBlend: {MM.GetFloat("_DstBlend")}\n    - _EmissionIntensity: {MM.GetFloat("_EmissionIntensity")}\n    - _GlossMapScale: {MM.GetFloat("_GlossMapScale")}\n    - _Glossiness: {MM.GetFloat("_Glossiness")}\n    - _GlossyReflections: {MM.GetFloat("_GlossyReflections")}\n    - _LineWidthS: {MM.GetFloat("_LineWidthS")}\n    - _Metallic: {MM.GetFloat("_Metallic")}\n    - _Mode: {MM.GetFloat("_Mode")}\n    - _NormalMapScale: {MM.GetFloat("_NormalMapScale")}\n    - _OcclusionStrength: {MM.GetFloat("_OcclusionStrength")}\n    - _OutlineOn: {MM.GetFloat("_OutlineOn")}\n    - _Parallax: {MM.GetFloat("_Parallax")}\n    - _Reflective: {MM.GetFloat("_Reflective")}\n    - _ReflectiveBlend: {MM.GetFloat("_ReflectiveBlend")}\n    - _ReflectiveMulOrAdd: {MM.GetFloat("_ReflectiveMulOrAdd")}\n    - _ShadowExtend: {MM.GetFloat("_ShadowExtend")}\n    - _ShadowExtendAnother: {MM.GetFloat("_ShadowExtendAnother")}\n    - _SmoothnessTextureChannel: {MM.GetFloat("_SmoothnessTextureChannel")}\n    - _SpeclarHeight: {MM.GetFloat("_SpeclarHeight")}\n    - _SpecularHighlights: {MM.GetFloat("_SpecularHighlights")}\n    - _SpecularPower: {MM.GetFloat("_SpecularPower")}\n    - _SpecularPowerNail: {MM.GetFloat("_SpecularPowerNail")}\n    - _SrcBlend: {MM.GetFloat("_SrcBlend")}\n    - _UVSec: {MM.GetFloat("_UVSec")}\n    - _UseDetailRAsSpecularMap: {MM.GetFloat("_UseDetailRAsSpecularMap")}\n    - _UseKKMetal: {MM.GetFloat("_UseKKMetal")}\n    - _UseLightColorSpecular: {MM.GetFloat("_UseLightColorSpecular")}\n    - _UseMatCapReflection: {MM.GetFloat("_UseMatCapReflection")}\n    - _UseRampForLights: {MM.GetFloat("_UseRampForLights")}\n    - _UseRampForSpecular: {MM.GetFloat("_UseRampForSpecular")}\n    - _ZWrite: {MM.GetFloat("_ZWrite")}\n    - _alpha_a: {MM.GetFloat("_alpha_a")}\n    - _alpha_b: {MM.GetFloat("_alpha_b")}\n    - _liquidbbot: {MM.GetFloat("_liquidbbot")}\n    - _liquidbtop: {MM.GetFloat("_liquidbtop")}\n    - _liquidface: {MM.GetFloat("_liquidface")}\n    - _liquidfbot: {MM.GetFloat("_liquidfbot")}\n    - _liquidftop: {MM.GetFloat("_liquidftop")}\n    - _notusetexspecular: {MM.GetFloat("_notusetexspecular")}\n    - _rimV: {MM.GetFloat("_rimV")}\n    - _rimpower: {MM.GetFloat("_rimpower")}");
                                    //colors(vector4)
                                    Text = Text.Replace($"    - _Clock: {{r: 0, g: 0, b: 0, a: 0}}\n    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _Color2: {{r: 0.11724187, g: 0, b: 1, a: 1}}\n    - _Color3: {{r: 0.5, g: 0.5, b: 0.5, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _OutlineColor: {{r: 0, g: 0, b: 0, a: 0}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}", $"    - _Clock: {{r: {MM.GetVector("_Clock").x}, g: {MM.GetVector("_Clock").y}, b: {MM.GetVector("_Clock").z}, a: {MM.GetVector("_Clock").w}}}\n    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _Color2: {{r: {MM.GetVector("_Color2").x}, g: {MM.GetVector("_Color2").y}, b: {MM.GetVector("_Color2").z}, a: {MM.GetVector("_Color2").w}}}\n    - _Color3: {{r: {MM.GetVector("_Color3").x}, g: {MM.GetVector("_Color3").y}, b: {MM.GetVector("_Color3").z}, a: {MM.GetVector("_Color3").w}}}\n    - _CustomAmbient: {{r: {MM.GetVector("_CustomAmbient").x}, g: {MM.GetVector("_CustomAmbient").y}, b: {MM.GetVector("_CustomAmbient").z}, a: {MM.GetVector("_CustomAmbient").w}}}\n    - _EmissionColor: {{r: {MM.GetVector("_EmissionColor").x}, g: {MM.GetVector("_EmissionColor").y}, b: {MM.GetVector("_EmissionColor").z}, a: {MM.GetVector("_EmissionColor").w}}}\n    - _OutlineColor: {{r: {MM.GetVector("_OutlineColor").x}, g: {MM.GetVector("_OutlineColor").y}, b: {MM.GetVector("_OutlineColor").z}, a: {MM.GetVector("_OutlineColor").w}}}\n    - _ShadowColor: {{r: {MM.GetVector("_ShadowColor").x}, g: {MM.GetVector("_ShadowColor").y}, b: {MM.GetVector("_ShadowColor").z}, a: {MM.GetVector("_ShadowColor").w}}}\n    - _SpecularColor: {{r: {MM.GetVector("_SpecularColor").x}, g: {MM.GetVector("_SpecularColor").y}, b: {MM.GetVector("_SpecularColor").z}, a: {MM.GetVector("_SpecularColor").w}}}");

                                    if (File.Exists(($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat")))
                                    {
                                        Logger.LogMessage($"{MatName} Already Exists! Checking if we should skip or make a new one");
                                        if (Text == File.ReadAllText(($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat")))
                                        {
                                            Logger.LogMessage($"{MatName} Has the same data as the material we are checking, Skipping!");
                                            continue;

                                        }
                                        Logger.LogMessage($"{MatName} Does not have the same data as the material we are checking, Writing a new one!");
                                        File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName}_{MM.GetInstanceID()} Assigned.mat", Text);
                                        continue;
                                    }
                                    //first instance
                                    File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat", Text);
                                    continue;
                                    //since we are using all the values instead of the ones that only appear in the real shader, we wull get alot of erros about a property not existing in the material, but its fine I think.
                                }

                                //Opaque

                                if (MM.shader.name == "Shader Forge/main_opaque")
                                {
                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\Templates\\";
                                    Logger.LogMessage($"Creating File {MatName} => {MatName} Assigned.mat");
                                    string Text = File.ReadAllText($"{InputDir}Opaque.mat");
                                    //texture scale and offsets
                                    Text = Text.Replace($"    - _AlphaMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _AnotherRamp:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _BumpMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _DetailAlbedoMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _DetailMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _DetailNormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _EmissionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _EmissionMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _LineMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _NormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _NormalMapDetail:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ReflectionMapCap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _Texture2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _Texture3:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _liquidmask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}", $"    - _AlphaMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_AlphaMask").x}, y: {MM.GetTextureScale("_AlphaMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_AlphaMask").x}, y: {MM.GetTextureOffset("_AlphaMask").y}}}\n    - _AnotherRamp:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_AnotherRamp").x}, y: {MM.GetTextureScale("_AnotherRamp").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_AnotherRamp").x}, y: {MM.GetTextureOffset("_AnotherRamp").y}}}\n    - _BumpMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_BumpMap").x}, y: {MM.GetTextureScale("_BumpMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_BumpMap").x}, y: {MM.GetTextureOffset("_BumpMap").y}}}\n    - _DetailAlbedoMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_DetailAlbedoMap").x}, y: {MM.GetTextureScale("_DetailAlbedoMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_DetailAlbedoMap").x}, y: {MM.GetTextureOffset("_DetailAlbedoMap").y}}}\n    - _DetailMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_DetailMask").x}, y: {MM.GetTextureScale("_DetailMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_DetailMask").x}, y: {MM.GetTextureOffset("_DetailMask").y}}}\n    - _DetailNormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_DetailNormalMap").x}, y: {MM.GetTextureScale("_DetailNormalMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_DetailNormalMap").x}, y: {MM.GetTextureOffset("_DetailNormalMap").y}}}\n    - _EmissionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_EmissionMap").x}, y: {MM.GetTextureScale("_EmissionMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_EmissionMap").x}, y: {MM.GetTextureOffset("_EmissionMap").y}}}\n    - _EmissionMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_EmissionMask").x}, y: {MM.GetTextureScale("_EmissionMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_EmissionMask").x}, y: {MM.GetTextureOffset("_EmissionMask").y}}}\n    - _LineMask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_LineMask").x}, y: {MM.GetTextureScale("_LineMask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_LineMask").x}, y: {MM.GetTextureOffset("_LineMask").y}}}\n    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_MainTex").x}, y: {MM.GetTextureScale("_MainTex").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_MainTex").x}, y: {MM.GetTextureOffset("_MainTex").y}}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_MetallicGlossMap").x}, y: {MM.GetTextureScale("_MetallicGlossMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_MetallicGlossMap").x}, y: {MM.GetTextureOffset("_MetallicGlossMap").y}}}\n    - _NormalMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_NormalMap").x}, y: {MM.GetTextureScale("_NormalMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_NormalMap").x}, y: {MM.GetTextureOffset("_NormalMap").y}}}\n    - _NormalMapDetail:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_NormalMapDetail").x}, y: {MM.GetTextureScale("_NormalMapDetail").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_NormalMapDetail").x}, y: {MM.GetTextureOffset("_NormalMapDetail").y}}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_OcclusionMap").x}, y: {MM.GetTextureScale("_OcclusionMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_OcclusionMap").x}, y: {MM.GetTextureOffset("_OcclusionMap").y}}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_ParallaxMap").x}, y: {MM.GetTextureScale("_ParallaxMap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_ParallaxMap").x}, y: {MM.GetTextureOffset("_ParallaxMap").y}}}\n    - _ReflectionMapCap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_ReflectionMapCap").x}, y: {MM.GetTextureScale("_ReflectionMapCap").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_ReflectionMapCap").x}, y: {MM.GetTextureOffset("_ReflectionMapCap").y}}}\n    - _Texture2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_Texture2").x}, y: {MM.GetTextureScale("_Texture2").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_Texture2").x}, y: {MM.GetTextureOffset("_Texture2").y}}}\n    - _Texture3:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_Texture3").x}, y: {MM.GetTextureScale("_Texture3").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_Texture3").x}, y: {MM.GetTextureOffset("_Texture3").y}}}\n    - _liquidmask:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_liquidmask").x}, y: {MM.GetTextureScale("_liquidmask").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_liquidmask").x}, y: {MM.GetTextureOffset("_liquidmask").y}}}");
                                    //floats
                                    Text = Text.Replace($"    - _AlphaOptionCutoff: 1\n    - _AnotherRampFull: 0\n    - _BumpScale: 1\n    - _CullOption: 0\n    - _Cutoff: 0.5\n    - _DetailBLineG: 0\n    - _DetailNormalMapScale: 1\n    - _DetailRLineR: 0\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _LineWidthS: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _NormalMapScale: 1\n    - _OcclusionStrength: 1\n    - _OutlineOn: 1\n    - _Parallax: 0.02\n    - _Reflective: 0.75\n    - _ReflectiveBlend: 0.05\n    - _ReflectiveMulOrAdd: 1\n    - _ShadowExtend: 1\n    - _ShadowExtendAnother: 0\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SpecularPowerNail: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseDetailRAsSpecularMap: 0\n    - _UseKKMetal: 1\n    - _UseLightColorSpecular: 1\n    - _UseMatCapReflection: 1\n    - _UseRampForLights: 1\n    - _UseRampForSpecular: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _notusetexspecular: 0\n    - _rimV: 0.5\n    - _rimpower: 0.5",$"    - _AlphaOptionCutoff: {MM.GetFloat("_AlphaOptionCutoff")}\n    - _AnotherRampFull: {MM.GetFloat("_AnotherRampFull")}\n    - _BumpScale: {MM.GetFloat("_BumpScale")}\n    - _CullOption: {MM.GetFloat("_CullOption")}\n    - _Cutoff: {MM.GetFloat("_Cutoff")}\n    - _DetailBLineG: {MM.GetFloat("_DetailBLineG")}\n    - _DetailNormalMapScale: {MM.GetFloat("_DetailNormalMapScale")}\n    - _DetailRLineR: {MM.GetFloat("_DetailRLineR")}\n    - _DstBlend: {MM.GetFloat("_DstBlend")}\n    - _EmissionIntensity: {MM.GetFloat("_EmissionIntensity")}\n    - _GlossMapScale: {MM.GetFloat("_GlossMapScale")}\n    - _Glossiness: {MM.GetFloat("_Glossiness")}\n    - _GlossyReflections: {MM.GetFloat("_GlossyReflections")}\n    - _LineWidthS: {MM.GetFloat("_LineWidthS")}\n    - _Metallic: {MM.GetFloat("_Metallic")}\n    - _Mode: {MM.GetFloat("_Mode")}\n    - _NormalMapScale: {MM.GetFloat("_NormalMapScale")}\n    - _OcclusionStrength: {MM.GetFloat("_OcclusionStrength")}\n    - _OutlineOn: {MM.GetFloat("_OutlineOn")}\n    - _Parallax: {MM.GetFloat("_Parallax")}\n    - _Reflective: {MM.GetFloat("_Reflective")}\n    - _ReflectiveBlend: {MM.GetFloat("_ReflectiveBlend")}\n    - _ReflectiveMulOrAdd: {MM.GetFloat("_ReflectiveMulOrAdd")}\n    - _ShadowExtend: {MM.GetFloat("_ShadowExtend")}\n    - _ShadowExtendAnother: {MM.GetFloat("_ShadowExtendAnother")}\n    - _SmoothnessTextureChannel: {MM.GetFloat("_SmoothnessTextureChannel")}\n    - _SpeclarHeight: {MM.GetFloat("_SpeclarHeight")}\n    - _SpecularHighlights: {MM.GetFloat("_SpecularHighlights")}\n    - _SpecularPower: {MM.GetFloat("_SpecularPower")}\n    - _SpecularPowerNail: {MM.GetFloat("_SpecularPowerNail")}\n    - _SrcBlend: {MM.GetFloat("_SrcBlend")}\n    - _UVSec: {MM.GetFloat("_UVSec")}\n    - _UseDetailRAsSpecularMap: {MM.GetFloat("_UseDetailRAsSpecularMap")}\n    - _UseKKMetal: {MM.GetFloat("_UseKKMetal")}\n    - _UseLightColorSpecular: {MM.GetFloat("_UseLightColorSpecular")}\n    - _UseMatCapReflection: {MM.GetFloat("_UseMatCapReflection")}\n    - _UseRampForLights: {MM.GetFloat("_UseRampForLights")}\n    - _UseRampForSpecular: {MM.GetFloat("_UseRampForSpecular")}\n    - _ZWrite: {MM.GetFloat("_ZWrite")}\n    - _alpha_a: {MM.GetFloat("_alpha_a")}\n    - _alpha_b: {MM.GetFloat("_alpha_b")}\n    - _liquidbbot: {MM.GetFloat("_liquidbbot")}\n    - _liquidbtop: {MM.GetFloat("_liquidbtop")}\n    - _liquidface: {MM.GetFloat("_liquidface")}\n    - _liquidfbot: {MM.GetFloat("_liquidfbot")}\n    - _liquidftop: {MM.GetFloat("_liquidftop")}\n    - _notusetexspecular: {MM.GetFloat("_notusetexspecular")}\n    - _rimV: {MM.GetFloat("_rimV")}\n    - _rimpower: {MM.GetFloat("_rimpower")}");
                                    //colors(vector4)
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: 0, g: 0, b: 2, a: 2}}\n    - _OutlineColor: {{r: 0, g: 0, b: 0, a: 0}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}",$"    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _CustomAmbient: {{r: {MM.GetVector("_CustomAmbient").x}, g: {MM.GetVector("_CustomAmbient").y}, b: {MM.GetVector("_CustomAmbient").z}, a: {MM.GetVector("_CustomAmbient").w}}}\n    - _EmissionColor: {{r: {MM.GetVector("_EmissionColor").x}, g: {MM.GetVector("_EmissionColor").y}, b: {MM.GetVector("_EmissionColor").z}, a: {MM.GetVector("_EmissionColor").w}}}\n    - _LiquidTiling: {{r: {MM.GetVector("_LiquidTiling").x}, g: {MM.GetVector("_LiquidTiling").y}, b: {MM.GetVector("_LiquidTiling").z}, a: {MM.GetVector("_LiquidTiling").w}}}\n    - _OutlineColor: {{r: {MM.GetVector("_OutlineColor").x}, g: {MM.GetVector("_OutlineColor").y}, b: {MM.GetVector("_OutlineColor").z}, a: {MM.GetVector("_OutlineColor").w}}}\n    - _ShadowColor: {{r: {MM.GetVector("_ShadowColor").x}, g: {MM.GetVector("_ShadowColor").y}, b: {MM.GetVector("_ShadowColor").z}, a: {MM.GetVector("_ShadowColor").w}}}\n    - _SpecularColor: {{r: {MM.GetVector("_SpecularColor").x}, g: {MM.GetVector("_SpecularColor").y}, b: {MM.GetVector("_SpecularColor").z}, a: {MM.GetVector("_SpecularColor").w}}}");

                                    if (File.Exists(($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat")))
                                    {
                                        Logger.LogMessage($"{MatName} Already Exists! Checking if we should skip or make a new one");
                                        if (Text == File.ReadAllText(($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat")))
                                        {
                                            Logger.LogMessage($"{MatName} Has the same data as the material we are checking, Skipping!");
                                            continue;

                                        }
                                        Logger.LogMessage($"{MatName} Does not have the same data as the material we are checking, Writing a new one!");
                                        File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName}_{MM.GetInstanceID()} Assigned.mat", Text);
                                        continue;
                                    }
                                    //first instance
                                    File.WriteAllText($"{InputDirMain}{CharacterName}\\{MatName} Assigned.mat", Text);
                                    continue;

                                }
                                Logger.LogMessage($"{MatName}'s Shader is not in the code, so we cant do anything, Shader: {MM.shader.name}");
                                continue;




                                if (MatName == "cf_m_body")
                                {
                                    //unused
                                    #region 
                                    /*string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
                                    string MatName = MatName;
                                    foreach (char c in invalid)
                                    {
                                        MatName = MatName.Replace(c.ToString(), "");
                                    }*/
                                    //create file and then close it.
                                    /* using (var file = File.CreateText(Environment.CurrentDirectory + "\\Materials\\" + CharacterName + "\\" + MatName + ".txt"))
                                     {
                                         Logger.LogMessage($"Creating File {MatName}.txt");
                                     }*/
                                    #endregion
                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\";
                                    Logger.LogMessage($"Creating File {MatName} => Template Body Assigned.mat");
                                    string Text = File.ReadAllText($"{InputDir}Template Body.mat");
                                    Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _LineWidthS: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _NormalMapScale: 1\n    - _OcclusionStrength: 1\n    - _OutlineOn: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: 1\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SpecularPowerNail: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseDetailRAsSpecularMap: 0\n    - _UseLightColorSpecular: 1\n    - _UseRampForLights: 1\n    - _UseRampForSpecular: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: 1\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: 0\n    - _nip_specular: 0.5\n    - _nipsize: 0.5\n    - _notusetexspecular: 0\n    - _rimV: 0\n    - _rimpower: 0.5\n    - _tex1mask: 0", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _GlossMapScale: 1\n    - _Glossiness: 0\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: 1\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SpecularPowerNail: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: 1\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: 0\n    - _nip_specular: 0.5\n    - _nipsize: 0.5\n    - _notusetexspecular: 0\n    - _rimV: 0\n    - _rimpower: 0.5\n    - _tex1mask: 0");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: 0, g: 0, b: 2, a: 2}}\n    - _OutlineColor: {{r: 0, g: 0, b: 0, a: 0}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor3: {{r: 1, g: 1, b: 1, a: 1}}", $"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: 0, g: 0, b: 2, a: 2}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor3: {{r: 1, g: 1, b: 1, a: 1}}");
                                    Text = Text.Replace($"    - _Texture2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _Texture3:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}",$"    - _Texture2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_Texture2").x}, y: {MM.GetTextureScale("_Texture2").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_Texture2").x}, y: {MM.GetTextureOffset("_Texture2").y}}}\n    - _Texture3:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_Texture3").x}, y: {MM.GetTextureScale("_Texture3").y}}}\n        m_Offset: {{x: 0, y: 0}}");
                                    Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _GlossMapScale: 1\n    - _Glossiness: 0\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: 1\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SpecularPowerNail: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: 1\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: 0\n    - _nip_specular: 0.5\n    - _nipsize: 0.5\n    - _notusetexspecular: 0\n    - _rimV: 0\n    - _rimpower: 0.5\n    - _tex1mask: 0", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: {MM.GetFloat("_DetailNormalMapScale")}\n    - _DstBlend: 0\n    - _GlossMapScale: 1\n    - _Glossiness: 0\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: {MM.GetFloat("_ShadowExtend")}\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: {MM.GetFloat("_SpecularHeight")}\n    - _SpecularHighlights: 1\n    - _SpecularPower: {MM.GetFloat("_SpecularPower")}\n    - _SpecularPowerNail: {MM.GetFloat("_SpecularPowerNail")}\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: {MM.GetFloat("_linetexon")}\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: {MM.GetFloat("_nip")}\n    - _nip_specular: {MM.GetFloat("_nip_specular")}\n    - _nipsize: {MM.GetFloat("_nipsize")}\n    - _notusetexspecular: {MM.GetFloat("_notusetexspecular")}\n    - _rimV: {MM.GetFloat("_rimV")}\n    - _rimpower: 0.5\n    - _tex1mask: {MM.GetFloat("_tex1mask")}");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: 0, g: 0, b: 2, a: 2}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor3: {{r: 1, g: 1, b: 1, a: 1}}", $"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: {MM.GetVector("_LiquidTiling").x}, g: {MM.GetVector("_LiquidTiling").y}, b: {MM.GetVector("_LiquidTiling").z}, a: {MM.GetVector("_LiquidTiling").w}}}\n    - _ShadowColor: {{r: {MM.GetVector("_ShadowColor").x}, g: {MM.GetVector("_ShadowColor").y}, b: {MM.GetVector("_ShadowColor").z}, a: {MM.GetVector("_ShadowColor").w}}}\n    - _SpecularColor: {{r: {MM.GetVector("_SpecularColor").x}, g: {MM.GetVector("_SpecularColor").y}, b: {MM.GetVector("_SpecularColor").z}, a: {MM.GetVector("_SpecularColor").w}}}\n    - _overcolor1: {{r: {MM.GetVector("_overcolor1").x}, g: {MM.GetVector("_overcolor1").y}, b: {MM.GetVector("_overcolor1").z}, a: {MM.GetVector("_overcolor1").w}}}\n    - _overcolor2: {{r: {MM.GetVector("_overcolor2").x}, g: {MM.GetVector("_overcolor2").y}, b: {MM.GetVector("_overcolor2").z}, a: {MM.GetVector("_overcolor2").w}}}\n    - _overcolor3: {{r: {MM.GetVector("_overcolor3").x}, g: {MM.GetVector("_overcolor3").y}, b: {MM.GetVector("_overcolor3").z}, a: {MM.GetVector("_overcolor3").w}}}");
                                    File.WriteAllText($"{InputDir}{CharacterName}\\Template Body Assigned.mat", Text);
                                }
                               if(MatName == "cf_m_face_00")
                                {
                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\";
                                    Logger.LogMessage($"Creating File {MatName} => Template Face Assigned.mat");
                                    string Text = File.ReadAllText($"{InputDir}Template Face.mat");
                                    Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _LineWidthS: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _NormalMapScale: 1\n    - _OcclusionStrength: 1\n    - _OutlineOn: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: 1\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SpecularPowerNail: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseDetailRAsSpecularMap: 0\n    - _UseLightColorSpecular: 1\n    - _UseRampForLights: 1\n    - _UseRampForSpecular: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: 1\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: 0\n    - _nip_specular: 0.5\n    - _nipsize: 0.5\n    - _notusetexspecular: 0\n    - _rimV: 0\n    - _rimpower: 0.5\n    - _tex1mask: 0", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _GlossMapScale: 1\n    - _Glossiness: 0\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: 1\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SpecularPowerNail: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: 1\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: 0\n    - _nip_specular: 0.5\n    - _nipsize: 0.5\n    - _notusetexspecular: 0\n    - _rimV: 0\n    - _rimpower: 0.5\n    - _tex1mask: 0");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: 0, g: 0, b: 2, a: 2}}\n    - _OutlineColor: {{r: 0, g: 0, b: 0, a: 0}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor3: {{r: 1, g: 1, b: 1, a: 1}}", $"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: 0, g: 0, b: 2, a: 2}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor3: {{r: 1, g: 1, b: 1, a: 1}}");
                                    Text = Text.Replace($"    - _Texture2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _Texture3:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}", $"    - _Texture2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_Texture2").x}, y: {MM.GetTextureScale("_Texture2").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_Texture2").x}, y: {MM.GetTextureOffset("_Texture2").y}}}\n    - _Texture3:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_Texture3").x}, y: {MM.GetTextureScale("_Texture3").y}}}\n        m_Offset: {{x: 0, y: 0}}");
                                    Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _GlossMapScale: 1\n    - _Glossiness: 0\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: 1\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: 0.98\n    - _SpecularHighlights: 1\n    - _SpecularPower: 0\n    - _SpecularPowerNail: 0\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: 1\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: 0\n    - _nip_specular: 0.5\n    - _nipsize: 0.5\n    - _notusetexspecular: 0\n    - _rimV: 0\n    - _rimpower: 0.5\n    - _tex1mask: 0", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: {MM.GetFloat("_DetailNormalMapScale")}\n    - _DstBlend: 0\n    - _GlossMapScale: 1\n    - _Glossiness: 0\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _ShadowExtend: {MM.GetFloat("_ShadowExtend")}\n    - _SmoothnessTextureChannel: 0\n    - _SpeclarHeight: {MM.GetFloat("_SpecularHeight")}\n    - _SpecularHighlights: 1\n    - _SpecularPower: {MM.GetFloat("_SpecularPower")}\n    - _SpecularPowerNail: {MM.GetFloat("_SpecularPowerNail")}\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _ZWrite: 1\n    - _alpha_a: 1\n    - _alpha_b: 1\n    - _linetexon: {MM.GetFloat("_linetexon")}\n    - _liquidbbot: 0\n    - _liquidbtop: 0\n    - _liquidface: 0\n    - _liquidfbot: 0\n    - _liquidftop: 0\n    - _nip: {MM.GetFloat("_nip")}\n    - _nip_specular: {MM.GetFloat("_nip_specular")}\n    - _nipsize: {MM.GetFloat("_nipsize")}\n    - _notusetexspecular: {MM.GetFloat("_notusetexspecular")}\n    - _rimV: {MM.GetFloat("_rimV")}\n    - _rimpower: 0.5\n    - _tex1mask: {MM.GetFloat("_tex1mask")}");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: 0, g: 0, b: 2, a: 2}}\n    - _ShadowColor: {{r: 0.628, g: 0.628, b: 0.628, a: 1}}\n    - _SpecularColor: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor3: {{r: 1, g: 1, b: 1, a: 1}}", $"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _LiquidTiling: {{r: {MM.GetVector("_LiquidTiling").x}, g: {MM.GetVector("_LiquidTiling").y}, b: {MM.GetVector("_LiquidTiling").z}, a: {MM.GetVector("_LiquidTiling").w}}}\n    - _ShadowColor: {{r: {MM.GetVector("_ShadowColor").x}, g: {MM.GetVector("_ShadowColor").y}, b: {MM.GetVector("_ShadowColor").z}, a: {MM.GetVector("_ShadowColor").w}}}\n    - _SpecularColor: {{r: {MM.GetVector("_SpecularColor").x}, g: {MM.GetVector("_SpecularColor").y}, b: {MM.GetVector("_SpecularColor").z}, a: {MM.GetVector("_SpecularColor").w}}}\n    - _overcolor1: {{r: {MM.GetVector("_overcolor1").x}, g: {MM.GetVector("_overcolor1").y}, b: {MM.GetVector("_overcolor1").z}, a: {MM.GetVector("_overcolor1").w}}}\n    - _overcolor2: {{r: {MM.GetVector("_overcolor2").x}, g: {MM.GetVector("_overcolor2").y}, b: {MM.GetVector("_overcolor2").z}, a: {MM.GetVector("_overcolor2").w}}}\n    - _overcolor3: {{r: {MM.GetVector("_overcolor3").x}, g: {MM.GetVector("_overcolor3").y}, b: {MM.GetVector("_overcolor3").z}, a: {MM.GetVector("_overcolor3").w}}}");
                                    File.WriteAllText($"{InputDir}{CharacterName}\\Template Face Assigned.mat", Text);

                                }
                               if(MatName.Contains("cf_m_eyeline_00_up"))
                                {
                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\";
                                    Logger.LogMessage($"Creating File {MatName} => Template EyeLineUp Assigned.mat");
                                    string Text = File.ReadAllText($"{InputDir}Template EyeLineUp.mat");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}",$"    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                    File.WriteAllText($"{InputDir}{CharacterName}\\Template EyeLineUp Assigned.mat", Text);


                                }
                                if (MatName.Contains("cf_m_eyeline_00_down"))
                                {
                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\";
                                    Logger.LogMessage($"Creating File {MatName} => Template EyeLineDown Assigned.mat");
                                    string Text = File.ReadAllText($"{InputDir}Template EyeLineDown.mat");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}", $"    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                    File.WriteAllText($"{InputDir}{CharacterName}\\Template EyeLineDown Assigned.mat", Text);


                                }
                                if (Mat.name == "cf_Ohitomi_L")
                                {
                                  
                                        string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\";
                                        Logger.LogMessage($"Creating File {MatName} => Template EyeWhite Assigned Left.mat");
                                        string Text = File.ReadAllText($"{InputDir}Template EyeWhite.mat");
                                        Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}", $"    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                        File.WriteAllText($"{InputDir}{CharacterName}\\Template EyeWhite Assigned Left.mat", Text);
                                        Sirome = true;
                                        

                                    


                                }
                                if (Mat.name == "cf_Ohitomi_R")
                                {

                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\";
                                    Logger.LogMessage($"Creating File {MatName} => Template EyeWhite Assigned Right.mat");
                                    string Text = File.ReadAllText($"{InputDir}Template EyeWhite.mat");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}", $"    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                    File.WriteAllText($"{InputDir}{CharacterName}\\Template EyeWhite Assigned Right.mat", Text);
                                    Sirome = true;
                                    




                                }
                                if (MatName.Contains("cf_m_mayuge_00"))
                                {
                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\";
                                    Logger.LogMessage($"Creating File {MatName} => Template EyeBrows Assigned.mat");
                                    string Text = File.ReadAllText($"{InputDir}Template EyeBrows.mat");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}", $"    - _Color: {{r: {MM.GetVector("_Color").x}, g: {MM.GetVector("_Color").y}, b: {MM.GetVector("_Color").z}, a: {MM.GetVector("_Color").w}}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                    File.WriteAllText($"{InputDir}{CharacterName}\\Template EyeBrows Assigned.mat", Text);
                                   
                                }
                                if (Mat.name == "cf_Ohitomi_L02")
                                {
                                   
                                        string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\";
                                        Logger.LogMessage($"Creating File {MatName} => Template Eye Assigned Left.mat");
                                        string Text = File.ReadAllText($"{InputDir}Template Eye.mat");
                                        Text = Text.Replace($"    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _expression:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _overtex1:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _overtex2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}", $"    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_MainTex").x}, y: {MM.GetTextureScale("_MainTex").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_MainTex").x}, y: {MM.GetTextureOffset("_MainTex").y}}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _expression:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_expression").x}, y: {MM.GetTextureScale("_expression").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_expression").x}, y: {MM.GetTextureOffset("_expression").y}}}\n    - _overtex1:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_overtex1").x}, y: {MM.GetTextureScale("_overtex1").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_overtex1").x}, y: {MM.GetTextureOffset("_overtex1").y}}}\n    - _overtex2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_overtex2").x}, y: {MM.GetTextureScale("_overtex2").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_overtex2").x}, y: {MM.GetTextureOffset("_overtex2").y}}}");
                                        Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _ExpressionDepth: 1\n    - _ExpressionSize: 0.35\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _SmoothnessTextureChannel: 0\n    - _SpecularHighlights: 1\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseRampForLights: 1\n    - _ZWrite: 1\n    - _exppower: 1\n    - _isHighLight: 0\n    - _rotation: 0", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _ExpressionDepth: {MM.GetFloat("_ExpressionDepth")}\n    - _ExpressionSize: {MM.GetFloat("_ExpressionSize")}\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _SmoothnessTextureChannel: 0\n    - _SpecularHighlights: 1\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseRampForLights: 1\n    - _ZWrite: 1\n    - _exppower: {MM.GetFloat("_exppower")}\n    - _isHighLight: {MM.GetFloat("_isHighLight")}\n    - _rotation: {MM.GetFloat("_rotation")}");
                                        Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}", $"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _overcolor1: {{r: {MM.GetVector("_overcolor1").x}, g: {MM.GetVector("_overcolor1").y}, b: {MM.GetVector("_overcolor1").z}, a: {MM.GetVector("_overcolor1").w}}}\n    - _overcolor2: {{r: {MM.GetVector("_overcolor2").x}, g: {MM.GetVector("_overcolor2").y}, b: {MM.GetVector("_overcolor2").z}, a: {MM.GetVector("_overcolor2").w}}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                        File.WriteAllText($"{InputDir}{CharacterName}\\Template Eye Assigned Left.mat", Text);
                                        
                                    

                                }
                                if (Mat.name == "cf_Ohitomi_R02")
                                {

                                    string InputDir = $"{Environment.CurrentDirectory}\\Materials\\Input\\";
                                    Logger.LogMessage($"Creating File {MatName} => Template Eye Assigned Right.mat");
                                    string Text = File.ReadAllText($"{InputDir}Template Eye.mat");
                                    Text = Text.Replace($"    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _expression:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _overtex1:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _overtex2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}", $"    - _MainTex:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_MainTex").x}, y: {MM.GetTextureScale("_MainTex").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_MainTex").x}, y: {MM.GetTextureOffset("_MainTex").y}}}\n    - _MetallicGlossMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _OcclusionMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _ParallaxMap:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n    - _expression:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_expression").x}, y: {MM.GetTextureScale("_expression").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_expression").x}, y: {MM.GetTextureOffset("_expression").y}}}\n    - _overtex1:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_overtex1").x}, y: {MM.GetTextureScale("_overtex1").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_overtex1").x}, y: {MM.GetTextureOffset("_overtex1").y}}}\n    - _overtex2:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: {MM.GetTextureScale("_overtex2").x}, y: {MM.GetTextureScale("_overtex2").y}}}\n        m_Offset: {{x: {MM.GetTextureOffset("_overtex2").x}, y: {MM.GetTextureOffset("_overtex2").y}}}");
                                    Text = Text.Replace($"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _ExpressionDepth: 1\n    - _ExpressionSize: 0.35\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _SmoothnessTextureChannel: 0\n    - _SpecularHighlights: 1\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseRampForLights: 1\n    - _ZWrite: 1\n    - _exppower: 1\n    - _isHighLight: 0\n    - _rotation: 0", $"    - _BumpScale: 1\n    - _Cutoff: 0.5\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _EmissionIntensity: 1\n    - _ExpressionDepth: {MM.GetFloat("_ExpressionDepth")}\n    - _ExpressionSize: {MM.GetFloat("_ExpressionSize")}\n    - _GlossMapScale: 1\n    - _Glossiness: 0.5\n    - _GlossyReflections: 1\n    - _Metallic: 0\n    - _Mode: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.02\n    - _SmoothnessTextureChannel: 0\n    - _SpecularHighlights: 1\n    - _SrcBlend: 1\n    - _UVSec: 0\n    - _UseRampForLights: 1\n    - _ZWrite: 1\n    - _exppower: {MM.GetFloat("_exppower")}\n    - _isHighLight: {MM.GetFloat("_isHighLight")}\n    - _rotation: {MM.GetFloat("_rotation")}");
                                    Text = Text.Replace($"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _overcolor1: {{r: 1, g: 1, b: 1, a: 1}}\n    - _overcolor2: {{r: 1, g: 1, b: 1, a: 1}}\n    - _shadowcolor: {{r: 0.6298235, g: 0.6403289, b: 0.747, a: 1}}", $"    - _Color: {{r: 1, g: 1, b: 1, a: 1}}\n    - _CustomAmbient: {{r: 0.6666667, g: 0.6666667, b: 0.6666667, a: 1}}\n    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}\n    - _overcolor1: {{r: {MM.GetVector("_overcolor1").x}, g: {MM.GetVector("_overcolor1").y}, b: {MM.GetVector("_overcolor1").z}, a: {MM.GetVector("_overcolor1").w}}}\n    - _overcolor2: {{r: {MM.GetVector("_overcolor2").x}, g: {MM.GetVector("_overcolor2").y}, b: {MM.GetVector("_overcolor2").z}, a: {MM.GetVector("_overcolor2").w}}}\n    - _shadowcolor: {{r: {MM.GetVector("_shadowcolor").x}, g: {MM.GetVector("_shadowcolor").y}, b: {MM.GetVector("_shadowcolor").z}, a: {MM.GetVector("_shadowcolor").w}}}");
                                    File.WriteAllText($"{InputDir}{CharacterName}\\Template Eye Assigned Right.mat", Text);



                                }



                                //more unused
                                #region
                                /*using (StreamWriter sw = System.IO.File.AppendText(Environment.CurrentDirectory + "\\Materials\\" + CharacterName + "\\" + MatName + ".txt"))
                                {
                                    sw.WriteLine($"\n{MatName}:");
                                    foreach (string Vector2 in P)
                                    {
                                        try
                                        {
                                            sw.WriteLine($"\n{Vector2}_Offset = {MM.GetTextureOffset(Vector2)}");
                                            sw.WriteLine($"\n{Vector2}_Scale = {MM.GetTextureScale(Vector2)}");
                                            sw.WriteLine($"\n_______________________");
                                        }
                                        catch
                                        {
                                            Logger.LogMessage($"{Vector2} not found in {MatName}");
                                        }
                                    }
                                    sw.WriteLine($"\n_______________________");
                                    foreach (string Vector4 in PV)
                                    {
                                        try
                                        {
                                            sw.WriteLine($"\n{Vector4} = {MM.GetVector(Vector4)}");
                                            sw.WriteLine($"\n_______________________");
                                        }
                                        catch
                                        {
                                            sw.WriteLine($"\n{Vector4} not found in {MatName}");
                                        }


                                    }
                                    sw.WriteLine($"\n_______________________");
                                    foreach(string Float in PF)
                                    {
                                        try
                                        {
                                            sw.WriteLine($"\n{Float} = {MM.GetFloat(Float)}");
                                        }
                                        catch
                                        {
                                            sw.WriteLine($"\n{Float} not found in {MatName}");
                                        }

                                    }
                                    sw.WriteLine($"\n\n");



                                }*/

                                /*string name = $"{Environment.CurrentDirectory}\\Materials\\{CharacterName}\\{MatName}";
                                var Filee = File.Create(Environment.CurrentDirectory + "\\Materials\\" + CharacterName + "\\" + MatName);
                                BepInEx.Logging.Logger.CreateLogSource("Bruggggg").LogMessage(MM.ToString());
                                byte[] TheBytes = ObjectToByteArray(MM);
                                
                                File.WriteAllBytes(Environment.CurrentDirectory + "\\Materials\\" + CharacterName + "\\" + MatName, TheBytes);*/
                                #endregion



                            }
                            //log
                            
                        }
                        Logger.LogMessage($"Done!");
                    }

                }
            }
            #endregion
        }




        // Convert an object to a byte array
        public static byte[] ObjectToByteArray(UnityEngine.Object obj)
        {
            BinaryFormatter bf = new BinaryFormatter();
            using (var ms = new MemoryStream())
            {
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        internal void Main()
        {
            Logger = base.Logger;

            SceneManager.sceneLoaded += SceneManager_sceneLoaded;
            
            //Start();
        }

        private void SceneManager_sceneLoaded(Scene s, LoadSceneMode lsm)
        {
            Logger.LogInfo($"Scene:{s.name}");
        }
    }
   
}