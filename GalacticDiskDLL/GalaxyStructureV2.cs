using UnityEngine;

namespace GalacticDisk
{
    // Keep these equations in step with GalaxyVolumeV2.shader.
    internal static class GalaxyStructureV2
    {
        internal static float Phase(GalaxySettings s,int arm) { return arm*2.399963f+(s.seed%997)*0.01337f; }
        internal static float Smooth(float a,float b,float x) { float t=Mathf.Clamp01((x-a)/(b-a)); return t*t*(3f-2f*t); }
        internal static float BranchWeight(GalaxySettings s,float r,int arm)
        {
            float start=0.36f+0.09f*Mathf.Sin(Phase(s,arm)*1.7f);
            return s.armBranchStrength*Smooth(start,start+0.16f,r)*(1f-Smooth(0.82f,1f,r));
        }
        internal static float Angle(GalaxySettings s,float radius,int arm,bool branch=false)
        {
            float r=radius/s.galaxyRadius, phase=Phase(s,arm);
            float angle=s.ArmAngle(radius)+arm*Mathf.PI*2f/Mathf.Max(1,s.armCount);
            angle+=s.armVariation*(0.55f*Mathf.Sin(r*11f+phase)+0.3f*Mathf.Sin(r*27f+phase*2f)+
                0.15f*Mathf.Sin(r*53f+phase*0.7f))*Smooth(s.armStartRadius*0.6f,0.8f,r);
            if (branch)
            {
                float start=0.36f+0.09f*Mathf.Sin(phase*1.7f);
                angle+=(arm%2==0?1f:-1f)*0.45f*Smooth(start,0.95f,r);
            }
            return angle;
        }
        internal static float ArmDensity(GalaxySettings s,float r,float angle,float offset,float width)
        {
            float density=0;
            for(int arm=0;arm<s.armCount;arm++)
            {
                float center=Angle(s,r*s.galaxyRadius,arm)+offset;
                float d=Mathf.DeltaAngle(center*Mathf.Rad2Deg,angle*Mathf.Rad2Deg)*Mathf.Deg2Rad;
                density+=Mathf.Exp(-0.5f*d*d/(width*width));
                float weight=BranchWeight(s,r,arm);
                if(weight>0)
                {
                    center=Angle(s,r*s.galaxyRadius,arm,true)+offset;
                    d=Mathf.DeltaAngle(center*Mathf.Rad2Deg,angle*Mathf.Rad2Deg)*Mathf.Deg2Rad;
                    density+=weight*Mathf.Exp(-0.5f*d*d/(width*width*0.6f));
                }
            }
            return density;
        }
    }
}
