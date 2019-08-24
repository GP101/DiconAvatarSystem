using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
 
// --------------------------------------------------
// Export2Maya ( Version 2.2.1 )
// By:	Michael Cook
// --------------------------------------------------
// IN PROGRESS:
//
// TO DO LIST:
//		Make the material export copy ALL textures it finds from the material
//		Add detail meshes + grass export for Terrains
//		Split the scripts up maybe to make it more organizable?
//		Animation Export
//		Look into being able to call Export2Maya from in game
//
// COMPLETED:
//		Fixed Material export to account gameobjects with no mesh renderer
//		Exporter Re-Written from the ground up
//			Much more organized than before
//		Standard Mesh Export
//			New Mesh Edge Generation - 3x faster!
//				Using StringBuilder to build strings of edges
//				Using List.BinarySearch for speed improvements
//			UV Export
//				Normal UV Export
//				Secondary UV Export
//		Skinned Mesh Export
//			Same features as Standard Mesh Export
//		Blendshapes
//			Single Frame Blendshape Support
//			Multi-Frame Blendshape Support
//		Material Export
//		Camera Export
//		Lights Export
//		Terrain Export
//			Standard Terrain Mesh Export
//			Standard Terrain UV Export
//			Standard Terrain Secondary UV Export
//			Standard Terrain Textures Export
//			Standard Terrain Splatmaps Export
//		Display Layer Export
//
// KNOWN ISSUES:
//		Camera export can be a bit unpredictable
//		BindPoses cannot be exported due to Unity limitations
//		Terrain Detail Mesh + Grass are not supported yet
// 
public class Export2Maya : EditorWindow {
    //{{ jintaeks 2019/07/24 11:53
    enum ECurveInfoCollectingState
    {
        COLLECTING_IDLE,    // idle
        COLLECTING_ACTION,  // collecting in action
    }
    //}} jintaeks 2019/07/24 11:53

    //{{ jintaeks 2019/07/24 11:53
    enum ECurveValueType
    {
        TYPE_X,
        TYPE_Y,
        TYPE_Z,
        TYPE_W,
    }
    //}} jintaeks 2019/07/24 11:53

    //{{ jintaeks 2019/07/24 11:53
    // @desc    base class for curve data
    // @date    jintaeks on 2019/07/24 15:16
    class CurveDataBase
    {
        public string pathName; // ex) "Sloth_Head2"
        public string typeName; // ex) "UnityEngine.SkinnedMeshRenderer"
        public string propertyName; // ex) "blendShape.blendShape2.eyeBlink_L"
        public string parentName; // parent node name
        public int dataSize; // number of key frames
        public float[] keyframeTime;
        public float interFrameTime;

        public virtual bool SetData(string propertyType_, AnimationCurve animCurve_) { return false; }
        public virtual bool IsDataValid() { return false; }
        public virtual void ConvertToMayaFormat() { }
        public virtual float GetValue(ECurveValueType valueType_, int valueIndex_) { return 0.0f; }
    }//class CurveDataBase
    //}} jintaeks 2019/07/24 11:53

    //{{ jintaeks 2019/07/24 11:53
    // @desc    curve position data
    // @date    jintaeks on 2019/07/24 15:17
    class CurveDataPosition : CurveDataBase
    {
        private bool isXAdded;
        private bool isYAdded;
        private bool isZAdded;
        public Vector3[] positionList;

        // constructor
        public CurveDataPosition(AnimationClipCurveData animClipCurveData_, float interFrameTime_, string parentName_)
        {
            UnityEngine.Debug.Assert(animClipCurveData_.curve.length > 0);
            pathName = animClipCurveData_.path;
            typeName = animClipCurveData_.type.ToString();
            propertyName = animClipCurveData_.propertyName;
            parentName = parentName_;
            dataSize = animClipCurveData_.curve.length;
            positionList = new Vector3[dataSize];
            isXAdded = false;
            isYAdded = false;
            isZAdded = false;
            keyframeTime = new float[dataSize];
            interFrameTime = interFrameTime_;
            for (int i = 0; i < dataSize; ++i)
            {
                keyframeTime[i] = animClipCurveData_.curve[i].time;
            }
        }//CurveDataPosition()

        public override bool SetData(string propertyType_, AnimationCurve animCurve_)
        {
            UnityEngine.Debug.Assert(dataSize == animCurve_.length);
            if (propertyType_ == "x")
            {
                for (int i = 0; i < dataSize; ++i)
                {
                    positionList[i].x = animCurve_[i].value;
                }
                isXAdded = true;
            }
            else if (propertyType_ == "y")
            {
                for (int i = 0; i < dataSize; ++i)
                {
                    positionList[i].y = animCurve_[i].value;
                }
                isYAdded = true;
            }
            else if (propertyType_ == "z")
            {
                for (int i = 0; i < dataSize; ++i)
                {
                    positionList[i].z = animCurve_[i].value;
                }
                isZAdded = true;
            }
            return (isXAdded && isYAdded && isZAdded);
        }//SetData()

        public override bool IsDataValid()
        {
            return (isXAdded && isYAdded && isZAdded) && dataSize >= 1;
        }//IsDataValid()

        public override void ConvertToMayaFormat()
        {
            UnityEngine.Debug.Assert(IsDataValid());
            for (int i = 0; i < dataSize; ++i)
            {
                positionList[i] = MayaConvert.MayaTranslation(positionList[i]);
            }//for
        }//ConvertToMayaFormat()

        public override float GetValue(ECurveValueType valueType_, int valueIndex_)
        {
            UnityEngine.Debug.Assert(valueIndex_ < dataSize);
            if (valueType_ == ECurveValueType.TYPE_X)
                return positionList[valueIndex_].x;
            else if (valueType_ == ECurveValueType.TYPE_Y)
                return positionList[valueIndex_].y;
            else if (valueType_ == ECurveValueType.TYPE_Z)
                return positionList[valueIndex_].z;
            return 0.0f;
        }//GetValue()
    }//class CurveDataPosition
    //}} jintaeks 2019/07/24 11:53

    //{{ jintaeks 2019/07/24 11:53
    // @desc    curve rotation data
    // @date    jintaeks on 2019/07/24 15:17
    class CurveDataRotation : CurveDataBase
    {
        private bool isXAdded;
        private bool isYAdded;
        private bool isZAdded;
        private bool isWAdded;
        public Quaternion[] rotationList; // [in] quaternion
        public Vector3[] eulerAngleList; // [out] Euler angles

        // constructor
        public CurveDataRotation(AnimationClipCurveData animClipCurveData_, float interFrameTime_, string parentName_)
        {
            UnityEngine.Debug.Assert(animClipCurveData_.curve.length > 0);
            pathName = animClipCurveData_.path;
            typeName = animClipCurveData_.type.ToString();
            propertyName = animClipCurveData_.propertyName;
            parentName = parentName_;
            dataSize = animClipCurveData_.curve.length;
            rotationList = new Quaternion[dataSize];
            eulerAngleList = new Vector3[dataSize];
            isXAdded = false;
            isYAdded = false;
            isZAdded = false;
            isWAdded = false;
            keyframeTime = new float[dataSize];
            interFrameTime = interFrameTime_;
            for (int i = 0; i < dataSize; ++i)
            {
                keyframeTime[i] = animClipCurveData_.curve[i].time;
                eulerAngleList[i] = new Vector3(0, 0, 0);
            }//for
        }//CurveDataRotation()

        public override bool SetData(string propertyType_, AnimationCurve animCurve_)
        {
            UnityEngine.Debug.Assert(dataSize == animCurve_.length);
            if (propertyType_ == "x")
            {
                for (int i = 0; i < dataSize; ++i)
                {
                    rotationList[i].x = animCurve_[i].value;
                }
                isXAdded = true;
            }
            else if (propertyType_ == "y")
            {
                for (int i = 0; i < dataSize; ++i)
                {
                    rotationList[i].y = animCurve_[i].value;
                }
                isYAdded = true;
            }
            else if (propertyType_ == "z")
            {
                for (int i = 0; i < dataSize; ++i)
                {
                    rotationList[i].z = animCurve_[i].value;
                }
                isZAdded = true;
            }
            else if (propertyType_ == "w")
            {
                for (int i = 0; i < dataSize; ++i)
                {
                    rotationList[i].w = animCurve_[i].value;
                }
                isWAdded = true;
            }
            return (isXAdded && isYAdded && isZAdded && isWAdded);
            //return (isXAdded && isYAdded && isZAdded );
        }//SetData()

        public override bool IsDataValid()
        {
            return (isXAdded && isYAdded && isZAdded && isWAdded) && dataSize >= 1;
            //return (isXAdded && isYAdded && isZAdded ) && dataSize >= 1;
        }//IsDataValid()

        public override void ConvertToMayaFormat()
        {
            UnityEngine.Debug.Assert(IsDataValid());
            for (int i = 0; i < dataSize; ++i)
            {
                eulerAngleList[i] = MayaConvert.MayaQuaternion(rotationList[i]);
                //eulerAngleList[i].x = rotationList[i].x;
                //eulerAngleList[i].y = rotationList[i].y;
                //eulerAngleList[i].z = rotationList[i].z;
            }//for
        }//ConvertToMayaFormat()

        public override float GetValue(ECurveValueType valueType_, int valueIndex_)
        {
            UnityEngine.Debug.Assert(valueIndex_ < dataSize);
            if (valueType_ == ECurveValueType.TYPE_X)
                return eulerAngleList[valueIndex_].x;
            else if (valueType_ == ECurveValueType.TYPE_Y)
                return eulerAngleList[valueIndex_].y;
            else if (valueType_ == ECurveValueType.TYPE_Z)
                return eulerAngleList[valueIndex_].z;

            return 0.0f;
        }//GetValue()
    }//class CurveDataRotation
    //}} jintaeks 2019/07/24 11:53

    //{{ jintaeks 2019/07/24 11:53
    class CurveDataScale : CurveDataPosition
    {
        public CurveDataScale(AnimationClipCurveData animClipCurveData_, float interFrameTime_, string parentName_)
            : base(animClipCurveData_, interFrameTime_, parentName_)
        {
        }//CurveDataScale()

        public override void ConvertToMayaFormat()
        {
            UnityEngine.Debug.Assert(IsDataValid());
            for (int i = 0; i < dataSize; ++i)
            {
                positionList[i] = positionList[i];
            }//for
        }//ConvertToMayaFormat()
    }//class CurveDataScale
    //}} jintaeks 2019/07/24 11:53

    //{{ jintaeks 2019/07/24 11:53
    // @desc    struct KNodeGenInfo
    // @date    jintaeks on 2019/07/24 19:25
    struct KNodeGenInfo
    {
        public int curveLength;
        public string[] splitPath;
        public string[] splitPropertyName;
        public string nodeTypeName;
        public string targetNodeNamePrefix;
        public string attrTargetNamePrefix;
        public float keyValueFactor;
        public string targetNodeNamePostfix;
        public string attrTargetPostfix;

        public void Initialize(string nodeTypeName_, int dataSize_, string pathName_
            , string propertyName_, string parentName_)
        {
            curveLength = dataSize_;
            splitPath = pathName_.Split('/');
            splitPropertyName = propertyName_.Split('.');
            nodeTypeName = "";
            targetNodeNamePrefix = "";
            attrTargetNamePrefix = "";
            keyValueFactor = 1.0f;
            targetNodeNamePostfix = "";
            attrTargetPostfix = "";

            // prepare prefix and postfix
            //
            nodeTypeName = nodeTypeName_;
            keyValueFactor = 1.0f;

            // set target node name
            //
            targetNodeNamePrefix = splitPath[0];
            // when 'targetNodeNamePrefix' is empty string, then set it's parent name
            if (targetNodeNamePrefix.Length == 0)
            {
                targetNodeNamePrefix = parentName_;
            }//if
            for (int j = 1; j < splitPath.Length; ++j)
            {
                targetNodeNamePrefix += "_";
                targetNodeNamePrefix += splitPath[j];
            }//for

            // set attribute target name
            //
            attrTargetNamePrefix = splitPath[0];
            // when 'targetNodeNamePrefix' is empty string, then set it's parent name
            if (attrTargetNamePrefix.Length == 0)
            {
                attrTargetNamePrefix = parentName_;
            }//if
            if (splitPath.Length >= 2)
            {
                attrTargetNamePrefix = "|" + attrTargetNamePrefix;
                for (int j = 1; j < splitPath.Length; ++j)
                {
                    attrTargetNamePrefix = attrTargetNamePrefix + "|" + splitPath[j];
                }//if.. else if..
            }
        }//Initialize()

        public void Generate(StringBuilder data_, CurveDataBase curveData_, int iValueIndex_ )
        {
            data_.Append("createNode ").Append(nodeTypeName).Append(" -n \"");
            data_.Append(targetNodeNamePrefix).Append(targetNodeNamePostfix).AppendLine("\";");
            data_.AppendLine("\tsetAttr \".tan\" 18;");
            data_.AppendLine("\tsetAttr \".wgt\" no;");
            data_.Append("\tsetAttr -s ").Append(curveLength.ToString())
                .Append(" \".ktv[0:").Append((curveLength - 1).ToString()).Append("]\"");

            for (int k = 0; k < curveLength; ++k)
            {
                int frameIndex = Mathf.RoundToInt(curveData_.keyframeTime[k] / curveData_.interFrameTime) + 1;
                float value = curveData_.GetValue((ECurveValueType)iValueIndex_, k ) * keyValueFactor;
                data_.Append(" ").Append(frameIndex.ToString()).Append(" ").Append(value);
            }//for
            data_.AppendLine(";");

            // connectAttr command.
            data_.Append("connectAttr \"").Append(targetNodeNamePrefix).Append(targetNodeNamePostfix).Append(".o\" ")
                .Append("\"").Append(attrTargetNamePrefix).Append(attrTargetPostfix).AppendLine("\";");
        }//Generate()
    }//struct KNodeGenInfo
    //}} jintaeks 2019/07/24 11:53
    
    // from line 400 to 427 omitted, _20190824_jintaeks



























    //{{ jintaeks 2019/07/17 19:48 // line 428
    AnimationClip animClip;
    ECurveInfoCollectingState ePositionCollectingState = ECurveInfoCollectingState.COLLECTING_IDLE;
    CurveDataPosition curveDataPosition;
    ECurveInfoCollectingState eScaleCollectingState = ECurveInfoCollectingState.COLLECTING_IDLE;
    CurveDataScale curveDataScale;
    ECurveInfoCollectingState eRotationCollectingState = ECurveInfoCollectingState.COLLECTING_IDLE;
    CurveDataRotation curveDataRotation;
    int iTempDebug = 0;
    //}} jintaeks 2019/07/17 19:48

    // --------------------------------------------------
    // Export2Maya GUI
    // --------------------------------------------------
    void OnGUI(){
		GUILayout.Label("Maya Version:", EditorStyles.boldLabel);
			MayaVersionIndex = EditorGUILayout.Popup(MayaVersionIndex, MayaVersions, GUILayout.MaxWidth(100));
		GUILayout.Label("Maya Units:", EditorStyles.boldLabel);
			MayaUnitsIndex = EditorGUILayout.Popup(MayaUnitsIndex, MayaUnits, GUILayout.MaxWidth(100));
        //{{ jintaeks 2019/07/17 19:53
        GUILayout.Label("Animation Clip:", EditorStyles.boldLabel);
        animClip = (AnimationClip)EditorGUILayout.ObjectField(animClip, typeof(AnimationClip), true);
        //}} jintaeks 2019/07/17 19:53
		GUILayout.Label("Begin Export:", EditorStyles.boldLabel);
		if(GUILayout.Button("Export Selection", GUILayout.Height(22))) ExportMaya();
		GUI.enabled = false;
		GUILayout.Label("Export2Maya - ver 2.2.1", EditorStyles.miniLabel);
		GUI.enabled = true;
	}
	
	// --------------------------------------------------
	// Export2Maya Main
	// --------------------------------------------------
	#region The Main Entry point
	void ExportMaya(){
		// from line 463 to 1328 omitted. qff
        // purchase Export2Maya at 'https://assetstore.unity.com/packages/tools/utilities/export2maya-17079'
































































































































































































































































































































































































































































































































































































































































































































































































































































































				// If there are blendshapes // 1329 qff
				//{{ jintaeks 2019/07/16 09:07
				if(NumBlendShapes > 0){
				//}} jintaeks 2019/07/16 09:07
					// Create Blendshape Nodes
					mObjectSet BlendShapeSet = new mObjectSet();
					BlendShapeSet.MayaName = GetUniqueGlobalNodeName("blendShapeSet");
					
					mGroupId BlendShapeGroupID = new mGroupId();
					BlendShapeGroupID.MayaName = GetUniqueGlobalNodeName("blendShapeGroupId");
					
					mGroupParts BlendShapeGroupParts = new mGroupParts();
					BlendShapeGroupParts.MayaName = GetUniqueGlobalNodeName("blendShapeGroupParts");
					BlendShapeGroupParts.ForceAllVerts = true;
					
					mBlendShape BlendShape = new mBlendShape();
					BlendShape.MayaName = GetUniqueGlobalNodeName("blendShape");
					BlendShape.UnityObject = DAGNodes[i].UnityObject;
					
					// Add to AUXNodesBuffer
					AUXNodes.Add(BlendShapeSet);
					AUXNodes.Add(BlendShapeGroupID);
					AUXNodes.Add(BlendShapeGroupParts);
					AUXNodes.Add(BlendShape);
					
					// Get new InstObjGroup connection index
					int BlendInstObj = SkinMeshShape.GetInstObjCount();
					
					// Create connections
					Connections.Append("connectAttr \"").Append(GetDAGPath(SkinMeshShape)).Append(".iog.og[").Append(BlendInstObj).Append("]\" \"").Append(BlendShapeSet.MayaName).AppendLine(".dsm\" -na;");
					Connections.Append("connectAttr \"").Append(BlendShapeSet.MayaName).Append(".mwc\" \"").Append(GetDAGPath(SkinMeshShape)).Append(".iog.og[").Append(BlendInstObj).AppendLine("].gco\";");
					Connections.Append("connectAttr \"").Append(BlendShape.MayaName).Append(".msg\" \"").Append(BlendShapeSet.MayaName).AppendLine(".ub[0]\";");
					Connections.Append("connectAttr \"").Append(BlendShapeGroupID.MayaName).Append(".msg\" \"").Append(BlendShapeSet.MayaName).AppendLine(".gn\" -na;");
					Connections.Append("connectAttr \"").Append(BlendShapeGroupID.MayaName).Append(".id\" \"").Append(GetDAGPath(SkinMeshShape)).Append(".iog.og[").Append(BlendInstObj).AppendLine("].gid\";");
					Connections.Append("connectAttr \"").Append(BlendShapeGroupID.MayaName).Append(".id\" \"").Append(BlendShape.MayaName).AppendLine(".ip[0].gi\";");
					Connections.Append("connectAttr \"").Append(BlendShapeGroupID.MayaName).Append(".id\" \"").Append(BlendShapeGroupParts.MayaName).AppendLine(".gi\";");
					Connections.Append("connectAttr \"").Append(BlendShapeGroupParts.MayaName).Append(".og\" \"").Append(BlendShape.MayaName).AppendLine(".ip[0].ig\";");
					Connections.Append("connectAttr \"").Append(BlendShape.MayaName).Append(".og[0]\" \"").Append(SkinClusterGroupParts.MayaName).AppendLine(".ig\";");
					
					// Connect TweakNode to BlendShapeGroupParts
					Connections.Append("connectAttr \"").Append(TweakNode.MayaName).Append(".og[0]\" \"").Append(BlendShapeGroupParts.MayaName).AppendLine(".ig\";");
				}
				// If no blendshapes
				else{
					// Connect TweakNode to SkinClusterGroupParts
					Connections.Append("connectAttr \"").Append(TweakNode.MayaName).Append(".og[0]\" \"").Append(SkinClusterGroupParts.MayaName).AppendLine(".ig\";");
				}
			}
			// from line 1377 to 1546 omitted. qff








































































































































































		// line 1546 qff
	}
	#endregion

	// from line 1550 to 2503 omitted. qff
























































































































































































































































































































































































































































































































































































































































































































































































































































































































































































    // line 2503 qff
    //{{ jintaeks 2019/07/24 14:12 2504 qff
    void _ExportPosition(CurveDataPosition curveDataPosition_, StringBuilder data)
    {
        UnityEngine.Debug.Assert(curveDataPosition_.pathName.Length >= 1);

        KNodeGenInfo nodeGenInfo = new KNodeGenInfo();

        nodeGenInfo.Initialize("animCurveTL", curveDataPosition_.dataSize, curveDataPosition_.pathName
            , curveDataPosition_.propertyName, curveDataPosition_.parentName);

        ////////////////////////////////////////////////////////////////////////////////
        //
        nodeGenInfo.attrTargetPostfix = ".tx";
        nodeGenInfo.targetNodeNamePostfix = "_LocalPositionx";
        nodeGenInfo.Generate(data, curveDataPosition_, (int)ECurveValueType.TYPE_X);

        ////////////////////////////////////////////////////////////////////////////////
        //
        nodeGenInfo.attrTargetPostfix = ".ty";
        nodeGenInfo.targetNodeNamePostfix = "_LocalPositiony";
        nodeGenInfo.Generate(data, curveDataPosition_, (int)ECurveValueType.TYPE_Y);

        ////////////////////////////////////////////////////////////////////////////////
        //
        nodeGenInfo.attrTargetPostfix = ".tz";
        nodeGenInfo.targetNodeNamePostfix = "_LocalPositionz";
        nodeGenInfo.Generate(data, curveDataPosition_, (int)ECurveValueType.TYPE_Z);
    }//_ExportPosition()
    //}} jintaeks 2019/07/24 14:12

    //{{ jintaeks 2019/07/24 14:12
    void _ExportScale(CurveDataScale curveDataScale_, StringBuilder data)
    {
        UnityEngine.Debug.Assert(curveDataScale_.pathName.Length >= 1);

        KNodeGenInfo nodeGenInfo = new KNodeGenInfo();

        nodeGenInfo.Initialize("animCurveTU", curveDataScale_.dataSize, curveDataScale_.pathName
            , curveDataScale_.propertyName, curveDataScale_.parentName);

        ////////////////////////////////////////////////////////////////////////////////
        //
        nodeGenInfo.attrTargetPostfix = ".sx";
        nodeGenInfo.targetNodeNamePostfix = "_LocalScalex";
        nodeGenInfo.Generate(data, curveDataScale_, (int)ECurveValueType.TYPE_X);

        ////////////////////////////////////////////////////////////////////////////////
        //
        nodeGenInfo.attrTargetPostfix = ".sy";
        nodeGenInfo.targetNodeNamePostfix = "_LocalScaley";
        nodeGenInfo.Generate(data, curveDataScale_, (int)ECurveValueType.TYPE_Y);

        ////////////////////////////////////////////////////////////////////////////////
        //
        nodeGenInfo.attrTargetPostfix = ".sz";
        nodeGenInfo.targetNodeNamePostfix = "_LocalScalez";
        nodeGenInfo.Generate(data, curveDataScale_, (int)ECurveValueType.TYPE_Z);
    }//_ExportScale()
    //}} jintaeks 2019/07/24 14:12

    //{{ jintaeks 2019/07/24 14:12
    void _ExportRotation(CurveDataRotation curveDataRotation_, StringBuilder data)
    {
        UnityEngine.Debug.Assert(curveDataRotation_.pathName.Length >= 1);

        KNodeGenInfo nodeGenInfo = new KNodeGenInfo();
        //nodeGenInfo.keyValueFactor = Mathf.Rad2Deg;

        nodeGenInfo.Initialize("animCurveTA", curveDataRotation_.dataSize, curveDataRotation_.pathName
            , curveDataRotation_.propertyName, curveDataRotation_.parentName);

        ////////////////////////////////////////////////////////////////////////////////
        //
        nodeGenInfo.attrTargetPostfix = ".rx";
        nodeGenInfo.targetNodeNamePostfix = "_LocalRotationx";
        nodeGenInfo.Generate(data, curveDataRotation_, (int)ECurveValueType.TYPE_X);

        ////////////////////////////////////////////////////////////////////////////////
        //
        nodeGenInfo.attrTargetPostfix = ".ry";
        nodeGenInfo.targetNodeNamePostfix = "_LocalRotationy";
        nodeGenInfo.Generate(data, curveDataRotation_, (int)ECurveValueType.TYPE_Y);

        ////////////////////////////////////////////////////////////////////////////////
        //
        nodeGenInfo.attrTargetPostfix = ".rz";
        nodeGenInfo.targetNodeNamePostfix = "_LocalRotationz";
        nodeGenInfo.Generate(data, curveDataRotation_, (int)ECurveValueType.TYPE_Z);
    }//_ExportRotation()
    //}} jintaeks 2019/07/24 14:12

    // --------------------------------------------------
    // Write Node
    // --------------------------------------------------
    void WriteNode(MayaNode Node, string filePath, string fileName){
		// from line 2599 to 3280 omitted. qff








































































































































































































































































































































































































































































































































































































































































































            // line 3280 qff
            //{{ jintaeks 2019/07/17 18:55 3281 qff
            //createNode animCurveTU -n "blendShape_envelope";
            //rename - uid "942E78E2-49D6-2A0E-DE79-97ADA68D0456";
            //setAttr ".tan" 2;
            //setAttr ".wgt" no;
            //setAttr - s 3 ".ktv[0:2]"  1 1 12 1 24 1;
            //qff
            if (animClip != null)
            {
                float interFrameTime = 1.0f / animClip.frameRate;
                AnimationClipCurveData[] animationClipCurveDataArray = AnimationUtility.GetAllCurves(animClip);
                for (int i = 0; i < animationClipCurveDataArray.Length; ++i)
                {
                    // qff
                    AnimationClipCurveData animationClipCurveData = animationClipCurveDataArray[i];
                    int curveLength = animationClipCurveData.curve.length;
                    string[] splitPath = animationClipCurveData.path.Split('/');
                    string[] splitPropertyName = animationClipCurveData.propertyName.Split('.');
                    //BlendShapeNames.Add(split[split.Length - 1]);

                    if (splitPath.Length == 0)
                        continue;
                    //if (splitPath[0].Length == 0)
                    //    continue;

                    string nodeTypeName = "";
                    string targetNodeName = "";
                    string attrSourceName = "";
                    string attrTargetName = "";
                    bool isBlendShapeCurve = false;
                    float keyValueFactor = 1.0f;
                    string targetNodeNamePostfix = "";

                    if (animationClipCurveData.type == typeof(SkinnedMeshRenderer))
                    {
                        if (splitPath[0].Length > 0 && splitPropertyName[0] == Node.MayaName)
                        {
                            nodeTypeName = "animCurveTU";
                            targetNodeName = splitPropertyName[splitPropertyName.Length - 1];
                            attrSourceName = targetNodeName + ".o";
                            attrTargetName = splitPropertyName[0];
                            isBlendShapeCurve = true;
                            keyValueFactor = 0.01f;
                        }
                        else
                        {
                            UnityEngine.Debug.Assert(false);
                            continue;
                        }//if.. else..

                        data.Append("createNode ").Append(nodeTypeName).Append(" -n \"");
                        data.Append(targetNodeName).AppendLine("\";");
                        data.AppendLine("\tsetAttr \".tan\" 18;");
                        data.AppendLine("\tsetAttr \".wgt\" no;");
                        data.Append("\tsetAttr -s ").Append(curveLength.ToString())
                            .Append(" \".ktv[0:").Append((curveLength - 1).ToString()).Append("]\"");

                        for (int k = 0; k < curveLength; ++k)
                        {
                            UnityEngine.Keyframe keyFrame = animationClipCurveData.curve[k];
                            int frameIndex = Mathf.RoundToInt(keyFrame.time / interFrameTime) + 1;
                            float value = keyFrame.value * keyValueFactor; // Unity value to Maya value
                            data.Append(" ").Append(frameIndex.ToString()).Append(" ").Append(value);
                        }//for
                        data.AppendLine(";");
                    }
                    else if (animationClipCurveData.type == typeof(Transform))
                    {
                        //string attrTargetPostfix = "";
                        if (splitPropertyName[0] == "m_LocalPosition")
                        {
                            if (ePositionCollectingState == ECurveInfoCollectingState.COLLECTING_IDLE)
                            {
                                curveDataPosition = new CurveDataPosition(animationClipCurveData, interFrameTime, Node.UnityObject.parent.name);
                                curveDataPosition.SetData(splitPropertyName[1], animationClipCurveData.curve);
                                ePositionCollectingState = ECurveInfoCollectingState.COLLECTING_ACTION;
                            }
                            else if (ePositionCollectingState == ECurveInfoCollectingState.COLLECTING_ACTION)
                            {
                                bool bIsSetDataAll = curveDataPosition.SetData(splitPropertyName[1], animationClipCurveData.curve);
                                if (bIsSetDataAll == true)
                                {
                                    curveDataPosition.ConvertToMayaFormat();
                                    _ExportPosition(curveDataPosition, data);
                                    ePositionCollectingState = ECurveInfoCollectingState.COLLECTING_IDLE;
                                }//if
                            }//if.. else if..
                        }
                        else if (splitPropertyName[0] == "m_LocalRotation")
                        {
                            //if (eRotationCollectingState == ECurveInfoCollectingState.COLLECTING_IDLE)
                            if( iTempDebug == 0 )
                            {
                                curveDataRotation = new CurveDataRotation(animationClipCurveData, interFrameTime, Node.UnityObject.parent.name);
                                curveDataRotation.SetData(splitPropertyName[1], animationClipCurveData.curve);
                                eRotationCollectingState = ECurveInfoCollectingState.COLLECTING_ACTION;
                                iTempDebug = 1;
                            }
                            //else if (eRotationCollectingState == ECurveInfoCollectingState.COLLECTING_ACTION)
                            else if( iTempDebug == 1 )
                            {
                                bool bIsSetDataAll = curveDataRotation.SetData(splitPropertyName[1], animationClipCurveData.curve);
                                if (bIsSetDataAll == true)
                                {
                                    curveDataRotation.ConvertToMayaFormat();
                                    _ExportRotation(curveDataRotation, data);
                                    eRotationCollectingState = ECurveInfoCollectingState.COLLECTING_IDLE;
                                    iTempDebug = 0;
                                }//if
                            }//if.. else if..
                        }
                        else if (splitPropertyName[0] == "m_LocalScale")
                        {
                            if (eScaleCollectingState == ECurveInfoCollectingState.COLLECTING_IDLE)
                            {
                                curveDataScale = new CurveDataScale(animationClipCurveData, interFrameTime, Node.UnityObject.parent.name);
                                curveDataScale.SetData(splitPropertyName[1], animationClipCurveData.curve);
                                eScaleCollectingState = ECurveInfoCollectingState.COLLECTING_ACTION;
                            }
                            else if (eScaleCollectingState == ECurveInfoCollectingState.COLLECTING_ACTION)
                            {
                                bool bIsSetDataAll = curveDataScale.SetData(splitPropertyName[1], animationClipCurveData.curve);
                                if (bIsSetDataAll == true)
                                {
                                    curveDataScale.ConvertToMayaFormat();
                                    _ExportScale(curveDataScale, data);
                                    eScaleCollectingState = ECurveInfoCollectingState.COLLECTING_IDLE;
                                }//if
                            }//if.. else if..
                        }
                        else
                        {
                            UnityEngine.Debug.Assert(false);
                        }//if.. else if.. else..
                    }//if.. else if..
                }//for

                // call 'connectAttr' node for BlendShape
                for (int i = 0; i < NumBlendShapes; i++)
                {
                    data.Append("connectAttr \"").Append(BlendShapeNames[i]).Append(".o\" ")
                        .Append("\"").Append(Node.MayaName).Append(".w[").Append(i.ToString())
                        .AppendLine("]\";");
                }//for
            }//if (animClip != null)
            //}} jintaeks 2019/07/17 18:55 3426 qff
        }
		// --------------------------------------------------
		// ObjectSet
		// --------------------------------------------------
		if(Node is mObjectSet){
			data.Append("createNode objectSet -n \"").Append(Node.MayaName).AppendLine("\";");
				data.AppendLine("\tsetAttr \".ihi\" 0;");
				data.AppendLine("\tsetAttr \".vo\" yes;");
		}
		
		// --------------------------------------------------
		// Tweak
		// --------------------------------------------------
		if(Node is mTweak){
			data.Append("createNode tweak -n \"").Append(Node.MayaName).AppendLine("\";");
		}
		
		// --------------------------------------------------
		// GroupParts
		// --------------------------------------------------
		// Tells Maya which faces are part of the group
		if(Node is mGroupParts){
			data.Append("createNode groupParts -n \"").Append(Node.MayaName).AppendLine("\";");
			data.AppendLine("\tsetAttr \".ihi\" 0;");
			
			// If this is a blendshape GroupParts node
			if(((mGroupParts)Node).ForceAllVerts){
				data.AppendLine("\tsetAttr \".ic\" -type \"componentList\" 1 \"vtx[*]\";");
			}
			// Else it is a SkinCluster GroupParts node
			else{
				// Get the submesh indices
				if(((mGroupParts)Node).SubMeshIndex != -1){			
					// Get the materials on the mesh
					int[] SubMeshTris = Node.UnityObject.gameObject.GetComponent<SkinnedMeshRenderer>().sharedMesh.GetTriangles(((mGroupParts)Node).SubMeshIndex);
					
					// We need to figure out which faces correspond to the SubMeshTris vertex indices
					// So go through the triangle list and see if the SubMeshTris values match
					// if they do, record this triangle index
					List<int> FaceIndices = new List<int>();
					int[] tris = Node.UnityObject.gameObject.GetComponent<SkinnedMeshRenderer>().sharedMesh.triangles;
					for(int i=0; i<SubMeshTris.Length; i+=3){
						for(int j=0; j<tris.Length; j+=3){
							if(SubMeshTris[i] == tris[j] && SubMeshTris[i+1] == tris[j+1] && SubMeshTris[i+2] == tris[j+2]) FaceIndices.Add(j/3);
						}
					}
				
					data.Append("\tsetAttr \".ic\" -type \"componentList\" ").Append(FaceIndices.Count);
					for(int i=0; i<FaceIndices.Count; i++) data.Append(" ").Append("\"f[").Append(FaceIndices[i]).Append("]\"");
					data.Append(";").AppendLine();
				}
				else{
					data.AppendLine("\tsetAttr \".ic\" -type \"componentList\" 1 \"vtx[*]\";");
				}
			}
		}
		
		// --------------------------------------------------
		// GroupID node
		// --------------------------------------------------
		if(Node is mGroupId){
			data.Append("createNode groupId -n \"").Append(Node.MayaName).AppendLine("\";");
				data.AppendLine("\tsetAttr \".ihi\" 0;");
		}
		
		// --------------------------------------------------
		// Shading Group
		// --------------------------------------------------
		if(Node is mShadingEngine){
			data.Append("createNode shadingEngine -n \"").Append(Node.MayaName).AppendLine("\";");
				data.AppendLine("\tsetAttr \".ihi\" 0;");
				data.AppendLine("\tsetAttr \".ro\" yes;");
		}

		// --------------------------------------------------
		// Material
		// --------------------------------------------------
		if(Node is mBlinn){
			data.Append("createNode blinn -n \"").Append(Node.MayaName).AppendLine("\";");
			
			// If Material is a regular material, write the attributes
			if(Node.MaterialType == MayaMaterialType.RegularMaterial){
				// Set color
				if(Node.UnityMaterial.HasProperty("_Color")){
					Color MainColor = Node.UnityMaterial.GetColor("_Color");
					data.Append("\tsetAttr \".c\" -type \"float3\" ").Append(MainColor.r.ToString()).Append(" ").Append(MainColor.g.ToString()).Append(" ").Append(MainColor.b.ToString()).AppendLine(";");
				}
	
				// Set specular color
				if(Node.UnityMaterial.HasProperty("_SpecColor")){
					Color SpecColor = Node.UnityMaterial.GetColor("_SpecColor");
					data.Append("\tsetAttr \".sc\" -type \"float3\" ").Append(SpecColor.r.ToString()).Append(" ").Append(SpecColor.g.ToString()).Append(" ").Append(SpecColor.b.ToString()).AppendLine(";");
				}
				
				// Set incandescence color
				if(Node.UnityMaterial.HasProperty("_EmissionColor")){
					Color EmissiveColor = Node.UnityMaterial.GetColor("_EmissionColor");
					data.Append("\tsetAttr \".ic\" -type \"float3\" ").Append(EmissiveColor.r.ToString()).Append(" ").Append(EmissiveColor.g.ToString()).Append(" ").Append(EmissiveColor.b.ToString()).AppendLine(";");
				}
			}
		}
		
		// --------------------------------------------------
		// File
		// --------------------------------------------------
		if(Node is mFile){
			string FullTexturePath = filePath + GetTextureNameExt(Node.UnityTexture);
			data.Append("createNode file -n \"").Append(Node.MayaName).AppendLine("\";");
				data.Append("\tsetAttr \".ftn\" -type \"string\" \"").Append(FullTexturePath).AppendLine("\";");
		}
		
		// --------------------------------------------------
		// TerrainFileAlpha
		// --------------------------------------------------
		if(Node is mTerrainFileAlpha){
			string FullTexturePath = filePath + ((mTerrainFileAlpha)Node).ImgName + ".png";
			data.Append("createNode file -n \"").Append(Node.MayaName).AppendLine("\";");
				data.Append("\tsetAttr \".ftn\" -type \"string\" \"").Append(FullTexturePath).AppendLine("\";");
		}
		
		// --------------------------------------------------
		// Ramp
		// --------------------------------------------------
		if(Node is mRamp){
			data.Append("createNode ramp -n \"").Append(Node.MayaName).AppendLine("\";");
				for(int i=0; i<((mRamp)Node).Colors.Count; i++){
					// Position
					data.Append("\tsetAttr \".cel[").Append(i.ToString()).AppendLine("].ep\" 0;");
					// Color
					data.Append("\tsetAttr \".cel[").Append(i.ToString()).Append("].ec\" -type \"float3\" ").Append(((mRamp)Node).Colors[i].r.ToString()).Append(" ").Append(((mRamp)Node).Colors[i].g.ToString()).Append(" ").Append(((mRamp)Node).Colors[i].b.ToString()).AppendLine(";");
				}
		}
		
		// --------------------------------------------------
		// Place2DTexture
		// --------------------------------------------------
		if(Node is mPlace2dTexture){
			data.Append("createNode place2dTexture -n \"").Append(Node.MayaName).AppendLine("\";");
				data.Append("\tsetAttr \".re\" -type \"float2\" ").Append(((mPlace2dTexture)Node).TexTiling.x).Append(" ").Append(((mPlace2dTexture)Node).TexTiling.y).AppendLine(";");
				data.Append("\tsetAttr \".of\" -type \"float2\" ").Append(((mPlace2dTexture)Node).TexOffset.x).Append(" ").Append(((mPlace2dTexture)Node).TexOffset.y).AppendLine(";");
		}
		
		// --------------------------------------------------
		// LayeredTexture
		// --------------------------------------------------
		if(Node is mLayeredTexture){
			int NumInputs = ((mLayeredTexture)Node).NumberOfInputs;
			
			data.Append("createNode layeredTexture -n \"").Append(Node.MayaName).AppendLine("\";");
				data.Append("\tsetAttr -s ").Append(NumInputs.ToString()).AppendLine(" \".cs\";");
				for(int i=0; i<NumInputs; i++){
					data.Append("\tsetAttr \".cs[").Append(i.ToString()).AppendLine("].a\" 1;");
					data.Append("\tsetAttr \".cs[").Append(i.ToString()).AppendLine("].bm\" 4;");
					data.Append("\tsetAttr \".cs[").Append(i.ToString()).AppendLine("].iv\" yes;");
				}
				data.AppendLine("\tsetAttr \".ail\" yes;");
		}
		
		// --------------------------------------------------
		// Bump2D
		// --------------------------------------------------
		if(Node is mBump2d){
			data.Append("createNode bump2d -n \"").Append(Node.MayaName).AppendLine("\";");
				// Set as "normal map"
				data.AppendLine("\tsetAttr \".bi\" 1;");
				// Provide 3d info (needs to be yes)
				data.AppendLine("\tsetAttr \".p3d\" yes;");
				data.Append("\tsetAttr \".bd\" ").Append(((mBump2d)Node).BumpAmount.ToString()).AppendLine(";");
		}
		
		// --------------------------------------------------
		// MaterialInfo
		// --------------------------------------------------
		if(Node is mMaterialInfo){
			data.Append("createNode materialInfo -n \"").Append(Node.MayaName).AppendLine("\";");
		}
		
		// --------------------------------------------------
		// SpotLight
		// --------------------------------------------------
		if(Node is mSpotLight){
			// Get Light data
			Light LightData = Node.UnityObject.gameObject.GetComponent<Light>();

			string LightShadow = "on";
			if(LightData.shadows == LightShadows.None) LightShadow = "off";		
		
			data.Append("createNode spotLight -n \"").Append(Node.MayaName).Append("\" -p \"").Append(GetDAGPath(Node.Parent)).AppendLine("\";");
				data.AppendLine("\tsetAttr -k off \".v\";");
				data .Append("\tsetAttr \".cl\" -type \"float3\" ").Append(LightData.color.r.ToString()).Append(" ").Append(LightData.color.g.ToString()).Append(" ").Append(LightData.color.b.ToString()).AppendLine(";");
				data.Append("\tsetAttr \".in\" ").Append(LightData.intensity).AppendLine(";");
				data.AppendLine("\tsetAttr \".de\" 1;");
				data.AppendLine("\tsetAttr \".dro\" 20;");
				data.Append("\tsetAttr \".ca\" ").Append(LightData.spotAngle).AppendLine(";");
				data.AppendLine("\tsetAttr \".pa\" 5;");
				data.Append("\tsetAttr \".urs\" ").Append(LightShadow).AppendLine(";");
		}
		
		// --------------------------------------------------
		// DirectionalLight
		// --------------------------------------------------
		if(Node is mDirectionalLight){
			// Get Light data
			Light LightData = Node.UnityObject.gameObject.GetComponent<Light>();

			string LightShadow = "on";
			if(LightData.shadows == LightShadows.None) LightShadow = "off";		
			
			data.Append("createNode directionalLight -n \"").Append(Node.MayaName).Append("\" -p \"").Append(GetDAGPath(Node.Parent)).AppendLine("\";");
				data.AppendLine("\tsetAttr -k off \".v\";");
				data .Append("\tsetAttr \".cl\" -type \"float3\" ").Append(LightData.color.r.ToString()).Append(" ").Append(LightData.color.g.ToString()).Append(" ").Append(LightData.color.b.ToString()).AppendLine(";");
				data.Append("\tsetAttr \".in\" ").Append(LightData.intensity).AppendLine(";");
				data.AppendLine("\tsetAttr \".de\" 1;");
				data.Append("\tsetAttr \".urs\" ").Append(LightShadow).AppendLine(";");
		}
		
		// --------------------------------------------------
		// PointLight
		// --------------------------------------------------
		if(Node is mPointLight){
			// Get Light data
			Light LightData = Node.UnityObject.gameObject.GetComponent<Light>();

			string LightShadow = "on";
			if(LightData.shadows == LightShadows.None) LightShadow = "off";		
			
			data.Append("createNode pointLight -n \"").Append(Node.MayaName).Append("\" -p \"").Append(GetDAGPath(Node.Parent)).AppendLine("\";");
				data.AppendLine("\tsetAttr -k off \".v\";");
				data .Append("\tsetAttr \".cl\" -type \"float3\" ").Append(LightData.color.r.ToString()).Append(" ").Append(LightData.color.g.ToString()).Append(" ").Append(LightData.color.b.ToString()).AppendLine(";");
				data.Append("\tsetAttr \".in\" ").Append(LightData.intensity).AppendLine(";");
				data.AppendLine("\tsetAttr \".de\" 1;");
				data.Append("\tsetAttr \".urs\" ").Append(LightShadow).AppendLine(";");
		}
		
		// --------------------------------------------------
		// AreaLight
		// --------------------------------------------------
		if(Node is mAreaLight){
			// Get Light data
			Light LightData = Node.UnityObject.gameObject.GetComponent<Light>();

			string LightShadow = "on";
			if(LightData.shadows == LightShadows.None) LightShadow = "off";		
			
			data.Append("createNode areaLight -n \"").Append(Node.MayaName).Append("\" -p \"").Append(GetDAGPath(Node.Parent)).AppendLine("\";");
				data.AppendLine("\tsetAttr -k off \".v\";");
				data .Append("\tsetAttr \".cl\" -type \"float3\" ").Append(LightData.color.r.ToString()).Append(" ").Append(LightData.color.g.ToString()).Append(" ").Append(LightData.color.b.ToString()).AppendLine(";");
				data.Append("\tsetAttr \".in\" ").Append(LightData.intensity).AppendLine(";");
				data.AppendLine("\tsetAttr \".de\" 1;");
				data.Append("\tsetAttr \".urs\" ").Append(LightShadow).AppendLine(";");
		}

		// --------------------------------------------------
		// Camera
		// --------------------------------------------------
		if(Node is mCamera){
			// Get camera properties
			Camera CamData = Node.UnityObject.gameObject.GetComponent<Camera>();
			
			// Calculate Field of View
			// Note - This is my hacky attempt at estimating Maya's field of view
			// There might be a better more mathematical way of doing this
			float FOV = CamData.fieldOfView * 0.3458333333333333f;
			
			data.Append("createNode camera -n \"").Append(Node.MayaName).Append("\" -p \"").Append(GetDAGPath(Node.Parent)).AppendLine("\";");
				data.AppendLine("\tsetAttr -k off \".v\";");
				data.AppendLine("\tsetAttr \".cap\" -type \"double2\" 1.41732 0.94488;");
				data.AppendLine("\tsetAttr \".ff\" 3;");
				data.Append("\tsetAttr \".fl\" ").Append(FOV).AppendLine(";");
				data.Append("\tsetAttr \".ncp\" ").Append(CamData.nearClipPlane).AppendLine(";");
				data.Append("\tsetAttr \".fcp\" ").Append(CamData.farClipPlane).AppendLine(";");
				data.AppendLine("\tsetAttr \".ow\" 30;");
				data.AppendLine("\tsetAttr \".imn\" -type \"string\" \"camera1\";");
				data.AppendLine("\tsetAttr \".den\" -type \"string\" \"camera1_depth\";");
				data.AppendLine("\tsetAttr \".man\" -type \"string\" \"camera1_mask\";");
		}
		
		// Write to file
		AppendToFile(filePath, fileName, data);
		data = null;
	}
	
	// from line 3709 to 4311 omitted. qff

























































































































































































































































































































































































































































































































































































































	// line 4311 qff
}