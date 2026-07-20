using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public List<AudioSource> _sources2D = new(), _sources3D = new();
    [SerializeField] AudioSource source3DPrefab;
    public AudioSource _musicSource;
    [SerializeField] private AudioMixerGroup _mixerGroupMusic;
    [SerializeField] private AudioMixerGroup _mixerGroupSFX;

    public AudioMixer mixer;
    [SerializeField] private AudioMixerVariable mixerVariable;

    private void Awake()
    {
        if (mixerVariable) mixerVariable.value = mixer;
        DontDestroyOnLoad(this.gameObject);
    }

    public void Play2DSFX(AudioClip clipToPlay, float clipVolume)
    {
        var source = Get2DAudioSource();
        source.volume = clipVolume;
        source.clip = clipToPlay;
        source.Play();
    }

    private AudioSource Get2DAudioSource()
    {
        foreach (var source in _sources2D)
        {
            if (!source.isPlaying) return source;
        }

        var newSource = gameObject.AddComponent<AudioSource>();
        newSource.outputAudioMixerGroup = _mixerGroupSFX;
        newSource.playOnAwake = false;

        _sources2D.Add(newSource);
        return newSource;
    }

    public void PlayMusic(AudioClip musicClip, float clipVolume)
    {
        _musicSource.volume = clipVolume;
        _musicSource.clip = musicClip;
        _musicSource.Play();
    }

    public IEnumerator FadeToNewMusic(AudioClip newClip, float duration)
    {
        if (_musicSource.clip == null)
        {
            _musicSource.clip = newClip;
            _musicSource.volume = 0f;
            _musicSource.Play();

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _musicSource.volume = Mathf.Lerp(0f, 1f, t / duration);
                yield return null;
            }

            _musicSource.volume = 1f;
            yield break;
        }

        float startVolume = _musicSource.volume;
        float tFadeOut = 0f;

        while (tFadeOut < duration * 0.5f)
        {
            tFadeOut += Time.unscaledDeltaTime;
            _musicSource.volume = Mathf.Lerp(startVolume, 0f, tFadeOut / (duration * 0.5f));
            yield return null;
        }

        _musicSource.Stop();
        _musicSource.clip = newClip;
        _musicSource.Play();

        float tFadeIn = 0f;
        while (tFadeIn < duration * 0.5f)
        {
            tFadeIn += Time.unscaledDeltaTime;
            _musicSource.volume = Mathf.Lerp(0f, 1f, tFadeIn / (duration * 0.5f));
            yield return null;
        }

        _musicSource.volume = 1f;
    }

    private AudioSource Get3DAudioSource()
    {
        foreach (var source in _sources3D)
        {
            if (!source.isPlaying) return source;
        }

        var newSource = Instantiate(source3DPrefab, transform);
        newSource.outputAudioMixerGroup = _mixerGroupSFX;
        newSource.playOnAwake = false;
        newSource.spatialBlend = 1;
        newSource.minDistance = 0;
        _sources3D.Add(newSource);
        return newSource;
    }


    public void Play3DSFX(AudioClip clipToPlay, Vector3 position, float clipVolume, float minRange, float maxRange)
    {
        var source = Get3DAudioSource();
        source.transform.position = position;
        source.volume = clipVolume;
        source.minDistance = minRange;
        source.maxDistance = maxRange;
        source.clip = clipToPlay;
        source.Play();
    }
}