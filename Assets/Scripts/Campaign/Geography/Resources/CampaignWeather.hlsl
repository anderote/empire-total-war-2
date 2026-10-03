#ifndef CAMPAIGN_WEATHER_INCLUDED
#define CAMPAIGN_WEATHER_INCLUDED
// Spherical position avoids an equirectangular seam or a latitude-dependent wind speed.
float MapHash(float3 p)
{
    p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);
}
float MapNoise(float3 p)
{
    float3 cell=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(lerp(MapHash(cell),MapHash(cell+float3(1,0,0)),f.x),lerp(MapHash(cell+float3(0,1,0)),MapHash(cell+float3(1,1,0)),f.x),f.y),
                lerp(lerp(MapHash(cell+float3(0,0,1)),MapHash(cell+float3(1,0,1)),f.x),lerp(MapHash(cell+float3(0,1,1)),MapHash(cell+1),f.x),f.y),f.z);
}
float MapCloud(float3 normal,float time,float coverage)
{
    float3 p=normal*28+float3(time*.009,0,time*.004);
    float n=MapNoise(p)*.56+MapNoise(p*2.03+17)*.28+MapNoise(p*4.11+31)*.16;
    return smoothstep(.76-coverage*.4,.91-coverage*.4,n);
}
#endif
