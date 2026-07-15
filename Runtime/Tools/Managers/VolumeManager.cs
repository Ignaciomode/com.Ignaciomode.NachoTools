using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class VolumeManager : MonoBehaviour
{
    [SerializeField] private AudioMixer _mixer;
    [SerializeField] private Slider _musicSlider, _sfxSlider, _masterSlider;

    private void Start()
    {
        _mixer = AudioManager.instance.mixer;
        if(_mixer)
            SetSavedValues();
    }

    public void SetSavedValues()
    {
        if (PlayerPrefs.HasKey("MasterVolume"))
        {
            OnChangeMasterVolume(PlayerPrefs.GetFloat("MasterVolume"));
            _masterSlider.value = PlayerPrefs.GetFloat("MasterVolume");
        }
        if (PlayerPrefs.HasKey("SFXVolume"))
        {
            OnChangeSFXVolume(PlayerPrefs.GetFloat("SFXVolume"));
            _sfxSlider.value = PlayerPrefs.GetFloat("SFXVolume");
        }
        if (PlayerPrefs.HasKey("MusicVolume"))
        {
            OnChangeMusicVolume(PlayerPrefs.GetFloat("MusicVolume"));
            _musicSlider.value = PlayerPrefs.GetFloat("MusicVolume");
        }
    }
    
    public void OnChangeMasterVolume(float value)
    {
        if(_mixer)
            _mixer.SetFloat("MasterVolume", Mathf.Log10(Mathf.Clamp(value / 100, 0.001f, 100)) * 20);
        PlayerPrefs.SetFloat("MasterVolume", value);
    }
    
    public void OnChangeSFXVolume(float value)
    {
        if(_mixer)
            _mixer.SetFloat("SFXVolume", Mathf.Log10(Mathf.Clamp(value / 100, 0.001f, 100)) * 20);
        PlayerPrefs.SetFloat("SFXVolume", value);
    }
    
    public void OnChangeMusicVolume(float value)
    {
        if(_mixer)
            _mixer.SetFloat("MusicVolume", Mathf.Log10(Mathf.Clamp(value / 100, 0.001f, 100)) * 20);
        PlayerPrefs.SetFloat("MusicVolume", value);
    }
}
