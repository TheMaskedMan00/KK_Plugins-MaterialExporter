using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using UnityEngine.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Linq;
using System.Xml;
//using static MaterialEditorAPI.MaterialAPI;
//using static MaterialEditorAPI.MaterialEditorPluginBase;

namespace KK_Plugins
{
    //[BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    [BepInPlugin(GUID, PluginName, Version)]

    public class MaterialExport : BaseUnityPlugin
    {
        public const string GUID = "com.masked.bepinex.materialexporter";
        public const string PluginName = "Material Exporter";
        public const string PluginNameInternal = "MaterialExport";
        public const string Version = "5.0";



        public string CharacterName;
        internal static new ManualLogSource Logger;
        //config stuff
        internal static ConfigEntry<string> ConfigExportPath { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ExportKeyBind { get; private set; }
        
        public static string ExportPathDefault = Path.Combine(Paths.GameRootPath, @"UserData\MaterialExporter");
        public static string ExportPath;
        
        private void Start()
        {
            // = BepInEx.Logging.Logger.CreateLogSource("MaskedMaterInfo");
            Logger.LogMessage("Started Material Exporter");
            WriteConfig();
            LoadXML();

        }

        //XML parser
        public static SortedDictionary<string, Dictionary<string, ShaderPropertyData>> XMLShaderProperties = new SortedDictionary<string, Dictionary<string, ShaderPropertyData>>();
        private static void LoadXML() //this is modified code from MaterialEditor to load XML files containting shader properties. I dont want to waste time decompiling more shaders and adding their properties to the internal list when someone has already done it. 
        {
            if(File.Exists(@"UserData\MaterialExporter\ShaderProperties.xml"))
            {
                Logger.LogInfo("XML file exists");

            }
            else
            {
                Logger.LogWarning("I can't seem to find ShaderProperties.xml in 'UserData/MaterialExporter' Some material properties might not export.\nPlease add the ShaderProperties.xml file into 'UserData/MaterialExporter', The file can be found in the github");
                return;


            }

            Logger.LogInfo($"Attempting to read XML at {Path.Combine(Paths.GameRootPath, @"UserData\MaterialExporter\ShaderProperties.xml")}");
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
                                                    //Logger.LogInfo($"_{propertyName}");
                                                    if (propertyType == ShaderPropertyType.Texture && !Props_Tex.Contains($"_{propertyName}"))
                                                    {
                                                        Props_Tex.Add($"_{propertyName}");
                                                        Logger.LogInfo($"Added _{propertyName} to Texture List");
                                                    }
                                                    else if (propertyType == ShaderPropertyType.Color && !Props_Color.Contains($"_{propertyName}"))
                                                    {
                                                        Props_Color.Add($"_{propertyName}");
                                                        Logger.LogInfo($"Added _{propertyName} to Color List");
                                                    }
                                                    else if (propertyType == ShaderPropertyType.Float && !Props_float.Contains($"_{propertyName}"))
                                                    {
                                                        Props_float.Add($"_{propertyName}");
                                                        Logger.LogInfo($"Added _{propertyName} to Float List");
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
                    Logger.LogInfo("ERROR: Stream is null for some reason, does the file exist?");
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
                    Logger.LogInfo($"Export Path has been Successfully Changed to {ExportPath}");
                }
                else
                {
                    Logger.LogWarning($"{ConfigExportPath.Value} Is not a valid path, Falling back to internal default");
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
        
        

        //list of known properties for all internal koikano shaders (Koikatsu Sunshine), properties for other shaders are loaded from the xml and added here at runtime.
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
        public static List<string> Props_Tex_G = new List<string> //I don't actually need this as you cannot save the texture without opening the assetbundle and exporting it, it is a lighting ramp. 
        {
            "_RampG",
        };

        //dont override if the directory already exists
        public string getNextFileName(string fileName) //https://stackoverflow.com/questions/1078003/how-would-you-make-a-unique-filename-by-adding-a-number
        {
            

            int i = 0;
            while (Directory.Exists(fileName))
            {
                if (i == 0)
                    fileName = fileName + "(" + ++i + ")";
                else
                    fileName = fileName.Replace("(" + i + ")", "(" + ++i + ")");
            }

            return fileName;
        }

        //get hash of material
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
        public List<Shader> ShaderTable = new List<Shader>();
        public void SaveYAMLMaterial(Material Mat, string savepath, Renderer Rend) //@MediaMoots @FlailingFog This is the main code for exporting the materials.
        {
            
            Logger.LogInfo($"Attempting to Write {Mat.name} to {savepath}");
            
            savepath = savepath.Replace(".mat", "15TEMP15.mat");
            

#if KKS //GetTexturePropertyNames does not exist in unity 5.x but it does exist in unity 2019.x
            if (ShaderTable != null)
            {
                if(ShaderTable.Count < 1)
                {
                    ShaderTable.Add(Mat.shader);
                    string[] TexProps = Mat.GetTexturePropertyNames();
                    foreach(string TexProp in TexProps)
                    {
                        //Logger.LogInfo(TexProp);
                        if(!Props_Tex.Contains(TexProp))
                        {
                            Logger.LogInfo($"{TexProp} Does not exist in our properties list, adding...");
                            Props_Tex.Add(TexProp);
                        }

                    }
                    
                }
                if(!ShaderTable.Contains(Mat.shader))
                {
                    ShaderTable.Add(Mat.shader);
                    string[] TexProps = Mat.GetTexturePropertyNames();
                    foreach (string TexProp in TexProps)
                    {
                        //Logger.LogInfo(TexProp);
                        if (!Props_Tex.Contains(TexProp))
                        {
                            Logger.LogInfo($"{TexProp} Does not exist in our properties list, adding...");
                            Props_Tex.Add(TexProp);
                        }

                    }

                }



            }
            
#endif
            var stream = File.CreateText(savepath);
            using(stream) //This is the Main Main part
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
                stream.WriteLine("  m_Name: Masked Material Exporter Material"); //unity will automaticlly change this when you import the material
                stream.WriteLine("  m_Shader: {fileID: 4800000, guid: 90c15467d827c0a489063235abdf25e7, type: 3}"); //need all these to be the same so the hash check works, change the shader in unity after
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
                        stream.WriteLine($"        m_Texture: {{fileID: 0}}");
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
                #region Write Junk Data that is only needed for this exporter and can be ignored in unity
                //This will put each texture's instance ID as a property to prevent identical materials with diff textures from being merged
                foreach (string s in Props_Tex)
                {
                    if (Mat.HasProperty(s))
                    {
                        stream.WriteLine($"    - {s}_JUNK: {(Mat.GetTexture(s) == null ? "0" : Mat.GetTexture(s).GetInstanceID().ToString())}");
                    }
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
                Logger.LogInfo($"{Mat.name}'s hash is {hash}");
                string newfilename = savepath.Replace("15TEMP15.mat", $"_{Rend.GetInstanceID()}.mat");
                File.Move(savepath, newfilename);
                return;


            }
            if (HashTable01 != null)
            {
                if(HashTable01.Contains(hash))
                {

                    File.Delete(savepath);
                    Logger.LogInfo($"{Mat.name}'s hash is {hash}");
                    Logger.LogInfo($" Hashtable Already Contains {hash} (A material with the exact same properties already exists), Discarding current material");
                    return;
                }
                else
                {
                    Logger.LogInfo($"{Mat.name}'s hash is {hash}");
                    HashTable01.Add(hash);
                    string newfilename = savepath.Replace("15TEMP15.mat", $"_{Rend.GetInstanceID()}.mat");
                    File.Move(savepath, newfilename);
                }

            }

            

          
           
        }



    



       
      
        public void Update()
        {
          




            #region Export Materials
            if (ExportKeyBind.Value.IsDown())
            {
                
                var Character = Resources.FindObjectsOfTypeAll<ChaControl>();
                foreach(ChaControl Charac in Character)
                {
                    CharacterName = Charac.fileParam.fullname;
                    break;

                }
                
                
               
                
                
                var Meshs = Resources.FindObjectsOfTypeAll<Transform>();
                //string InputDirMain = $"{Environment.CurrentDirectory}\\Materials\\Input\\";
                string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()) + "(){}[]$#@%^&*!;:<>\\/";
                string FinalExportPath = string.Empty;
                /*if (!Directory.Exists($"{InputDirMain}{CharacterName}"))
                {
                    Directory.CreateDirectory($"{InputDirMain}{CharacterName}");

                }*/
                if(Directory.Exists(ExportPath + "\\Materials\\" + CharacterName))
                {
                    FinalExportPath = getNextFileName(ExportPath + "\\Materials\\" + CharacterName);

                }
                if (!Directory.Exists(ExportPath + "\\Materials\\" + CharacterName))
                {
                    Directory.CreateDirectory(ExportPath + "\\Materials\\" + CharacterName);
                    FinalExportPath = ExportPath + "\\Materials\\" + CharacterName;

                }
                

                foreach (Transform M in Meshs)
                {
                    if(M.gameObject.name == "chaF_001" || M.gameObject.name == "chaM_001")
                    {
                        //let hope this grabs both mesh and skinned mesh (Edit: It does)
                        foreach(Renderer Rend in M.GetComponentsInChildren<Renderer>(true))
                        {
                            
                            foreach (Material MM in Rend.sharedMaterials)
                            {



                                

                              
                                string MatName = MM.name;
                                foreach (char c in invalid)
                                {
                                    MatName = MatName.Replace(c.ToString(), "");
                                }
                                


                                

                                if (Rend.name == "cf_Ohitomi_L") MatName = "EyeWhite_Left";
                                else if (Rend.name == "cf_Ohitomi_R") MatName = "EyeWhite_Right";
                                else if (Rend.name == "cf_Ohitomi_L02") MatName = "Eye_Left";
                                else if (Rend.name == "cf_Ohitomi_R02") MatName = "Eye_Right";
                                
                                

                                SaveYAMLMaterial(MM, $"{FinalExportPath}\\{MatName}.mat",Rend);
                               
                            }
                            //log
                            
                        }
                        Logger.LogMessage($"Done!");
                    }

                }
            }
#endregion
        }




        

        internal void Main()
        {
            Logger = base.Logger;

            SceneManager.sceneLoaded += SceneManager_sceneLoaded;
            
            //Start();
        }

        private void SceneManager_sceneLoaded(Scene s, LoadSceneMode lsm)
        {
            //Logger.LogInfo($"Scene:{s.name}");
        }
    }



    //this code is just copy pasted from Material Editor so I can load existing XML files containting all the 'known' shader properties, This makes it so I don't need to re compile the plugin when I find more shader properties that I need to add. I also Didn't want to add a dependency to MaterialEditor in the release cause then i will have to include the whole material editor in the repository.
    public class ShaderData
    {
        public string ShaderName;
        public Shader Shader;
        public int? RenderQueue;
        public bool ShaderOptimization;

        public ShaderData(Shader shader, string shaderName, string renderQueue = "", string shaderOptimization = null)
        {
            Shader = shader;
            ShaderName = shaderName;

            if (renderQueue.IsNullOrEmpty())
                RenderQueue = null;
            else if (int.TryParse(renderQueue, out int result))
                RenderQueue = result;
            else
                RenderQueue = null;

            if (bool.TryParse(shaderOptimization, out bool shaderOptimizationBool))
                ShaderOptimization = shaderOptimizationBool;
            else
                ShaderOptimization = true;
        }
    }

    public class ShaderPropertyData
    {
        public string Name;
        public ShaderPropertyType Type;
        public string DefaultValue;
        public string DefaultValueAssetBundle;
        public float? MinValue;
        public float? MaxValue;

        public ShaderPropertyData(string name, ShaderPropertyType type, string defaultValue = null, string defaultValueAB = null, string minValue = null, string maxValue = null)
        {
            Name = name;
            Type = type;
            DefaultValue = defaultValue.IsNullOrEmpty() ? null : defaultValue;
            DefaultValueAssetBundle = defaultValueAB.IsNullOrEmpty() ? null : defaultValueAB;
            if (!minValue.IsNullOrWhiteSpace() && !maxValue.IsNullOrWhiteSpace())
            {
                if (float.TryParse(minValue, out float min) && float.TryParse(maxValue, out float max))
                {
                    MinValue = min;
                    MaxValue = max;
                }
            }
        }
    }

    public enum ShaderPropertyType
    {
        /// <summary>
        /// Texture
        /// </summary>
        Texture,
        /// <summary>
        /// Color, Vector4, Vector3, Vector2
        /// </summary>
        Color,
        /// <summary>
        /// Float, Int, Bool
        /// </summary>
        Float
    }
    /// <summary>
    /// Properties of a renderer that can be set
    /// </summary>
    public enum RendererProperties
    {
        /// <summary>
        /// Whether the renderer is enabled
        /// </summary>
        Enabled,
        /// <summary>
        /// ShadowCastingMode of the renderer
        /// </summary>
        ShadowCastingMode,
        /// <summary>
        /// Whether the renderer will receive shadows cast by other objects
        /// </summary>
        ReceiveShadows
    }

}