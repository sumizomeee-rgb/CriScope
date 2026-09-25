namespace CriScope.Core;

// Numeric wire identifiers and scalar encodings for the verified CRI monitor protocol.
// Interoperability facts only; no SDK implementation or licensed binaries are included.
internal static class NativeSchema
{
    internal static readonly (string Name, string Type)[] Parameters =
    [
        ("#CRIATOMDEF", "CHAR"), // 0
        ("#CRIATOM", "CHAR"), // 1
        ("TimeStamp[usec]", "INT64"), // 2
        ("ThreadId", "UINTPTR"), // 3
        ("thread_model", "INT8"), // 4
        ("server_frequency", "FLOAT32"), // 5
        ("parameter_update_interval", "INT16"), // 6
        ("max_virtual_voices", "INT16"), // 7
        ("max_voice_limit_groups", "INT16"), // 8
        ("max_categories", "INT16"), // 9
        ("max_sequences", "INT16"), // 10
        ("max_tracks", "INT16"), // 11
        ("max_track_items", "INT16"), // 12
        ("max_aisac_auto_modulations", "INT16"), // 13
        ("max_pitch", "FLOAT32"), // 14
        ("coordinate_system", "INT8"), // 15
        ("rng_if", "UINTPTR"), // 16
        ("fs_config", "UINTPTR"), // 17
        ("context", "UINTPTR"), // 18
        ("output_channels", "INT8"), // 19
        ("output_sampling_rate", "INT32"), // 20
        ("sound_renderer_type", "INT8"), // 21
        ("num_mixers", "INT16"), // 22
        ("max_voices", "INT16"), // 23
        ("max_input_channels", "INT8"), // 24
        ("max_sampling_rate", "INT32"), // 25
        ("identifier", "INT32"), // 26
        ("max_streams", "INT32"), // 27
        ("max_bps", "INT32"), // 28
        ("CriAtomDbasId", "INT32"), // 29
        ("max_path", "INT16"), // 30
        ("max_files", "INT32"), // 31
        ("cache_size", "INT32"), // 32
        ("CriAtomStreamingCacheId", "UINTPTR"), // 33
        ("num_voices", "INT32"), // 34
        ("max_channels", "INT8"), // 35
        ("streaming_flag", "INT8"), // 36
        ("decode_latency", "INT32"), // 37
        ("CriAtomExVoicePoolHn", "UINTPTR"), // 38
        ("allocation_method", "INT8"), // 39
        ("max_path_strings", "INT16"), // 40
        ("updates_time", "INT8"), // 41
        ("CriAtomExPlayerHn", "UINTPTR"), // 42
        ("id", "INT32"), // 43
        ("parameter_type", "INT8"), // 44
        ("key", "INT64"), // 45
        ("CriAtomDecrypterHn", "UINTPTR"), // 46
        ("work", "UINTPTR"), // 47
        ("work_size", "INT32"), // 48
        ("CriAtomEx3dSourceHn", "UINTPTR"), // 49
        ("CriAtomEx3dListenerHn", "UINTPTR"), // 50
        ("CriAtomExPlaybackId", "INT32"), // 51
        ("CriAtomExFaderConfig", "UINTPTR"), // 52
        ("CriAtomExAcfConfig", "UINTPTR"), // 53
        ("acf_data", "UINTPTR"), // 54
        ("acf_data_size", "INT32"), // 55
        ("CriFsBinderHn", "UINTPTR"), // 56
        ("path", "CHAR"), // 57
        ("acb_data", "UINTPTR"), // 58
        ("acb_data_size", "INT32"), // 59
        ("awb_path", "CHAR"), // 60
        ("awb_id", "INT32"), // 61
        ("acb_path", "CHAR"), // 62
        ("acb_id", "INT32"), // 63
        ("CriAtomExAcbHn", "UINTPTR"), // 64
        ("sw", "INT8"), // 65
        ("CriAtomExResumeMode", "INT8"), // 66
        ("error_string", "CHAR"), // 67
        ("CriAtomSoundPlaybackId", "INT32"), // 68
        ("CriAtomSoundPlayerHn", "UINTPTR"), // 69
        ("CriAtomAwbHn", "UINTPTR"), // 70
        ("CriAtomExCueId", "INT32"), // 71
        ("cue_name", "CHAR"), // 72
        ("CriAtomExCueIndex", "INT32"), // 73
        ("buffer", "UINTPTR"), // 74
        ("size", "INT32"), // 75
        ("CriAtomExWaveId", "INT32"), // 76
        ("CriAtomAwbHn for Memory", "UINTPTR"), // 77
        ("CriAtomAwbHn for Stream", "UINTPTR"), // 78
        ("CriAtomExTweenHn", "UINTPTR"), // 79
        ("CriAtomExConfig", "UINTPTR"), // 80
        ("CriAtomExAsrConfig", "UINTPTR"), // 81
        ("CriAtomExHcaMxConfig", "UINTPTR"), // 82
        ("CriAtomDbasConfig", "UINTPTR"), // 83
        ("CriAtomStreamingCacheConfig", "UINTPTR"), // 84
        ("CriAtomExStandardVoicePoolConfig", "UINTPTR"), // 85
        ("CriAtomExAdxVoicePoolConfig", "UINTPTR"), // 86
        ("CriAtomExAhxVoicePoolConfig", "UINTPTR"), // 87
        ("CriAtomExHcaVoicePoolConfig", "UINTPTR"), // 88
        ("CriAtomExHcaMxVoicePoolConfig", "UINTPTR"), // 89
        ("CriAtomExWaveVoicePoolConfig", "UINTPTR"), // 90
        ("CriAtomExRawPcmVoicePoolConfig", "UINTPTR"), // 91
        ("CriAtomExPlayerConfig", "UINTPTR"), // 92
        ("CriAtomExTweenConfig", "UINTPTR"), // 93
        ("CriAtomDecrypterConfig", "UINTPTR"), // 94
        ("CriAtomEx3dSourceConfig", "UINTPTR"), // 95
        ("CriAtomEx3dListenerConfig", "UINTPTR"), // 96
        ("CriAtomExAdpcmVoicePoolConfig_3DS", "UINTPTR"), // 97
        ("CriAtomExAdpcmVoicePoolConfig_WII", "UINTPTR"), // 98
        ("CriAtomExVagVoicePoolConfig_PSP", "UINTPTR"), // 99
        ("CriAtomExAtrac3VoicePoolConfig_PSP", "UINTPTR"), // 100
        ("CriAtomExVagVoicePoolConfig_VITA", "UINTPTR"), // 101
        ("CriAtomExAt9VoicePoolConfig_VITA", "UINTPTR"), // 102
        ("num_groups", "INT16"), // 103
        ("voices_per_group", "INT16"), // 104
        ("num_category_groups", "INT16"), // 105
        ("num_categories", "INT16"), // 106
        ("reserved", "INT32"), // 107
        ("CriAtomExFaderHn", "UINTPTR"), // 108
        ("Guid", "GUID"), // 109
        ("parent CriAtomExPlaybackId", "INT32"), // 110
        ("CriAtomPlayerPoolPlayerInfo", "UINTPTR"), // 111
        ("CriAtomSoundElementHn", "UINTPTR"), // 112
        ("CriAtomSoundVoiceHn", "UINTPTR"), // 113
        ("cause CriAtomExPlaybackId", "INT32"), // 114
        ("Index", "INT16"), // 115
        ("NumAllPlaybacks", "INT16"), // 116
        ("NumPlaybacks", "INT16"), // 117
        ("categories_per_playback", "INT8"), // 118
        ("enable_voice_priority_decay", "INT8"), // 119
        ("volume", "FLOAT32"), // 120
        ("CriAtomSoundElementId", "INT32"), // 121
        ("CriAtomSoundVoiceId", "INT32"), // 122
        ("Acb Name", "CHAR"), // 123
        ("CriAtomPlayerPoolPlayerInfoId", "INT32"), // 124
        ("Aisac Control", "FLOAT32"), // 125
        ("Track No", "INT16"), // 126
        ("Mute", "INT8"), // 127
        ("Result", "INT8"), // 128
        ("Log Record Mode", "INT32"), // 129
        ("NumCh", "INT8"), // 130
        ("NumLoaders", "INT32"), // 131
        ("NumPlayers", "INT32"), // 132
        ("Status", "INT8"), // 133
        ("PlayingTime", "INT32"), // 134
        ("DspBusSpectra", "128"), // 135
        ("CpuLoad", "FLOAT32"), // 136
        ("NumUsedVoices", "INT32"), // 137
        ("SequencePlaybackPosition", "INT64"), // 138
        ("CallbackValue", "INT32"), // 139
        ("CallbackString", "CHAR"), // 140
        ("PeakLevel", "FLOAT32"), // 141
        ("RmsLevel", "FLOAT32"), // 142
        ("PeakHoldLevel", "FLOAT32"), // 143
        ("RequestId", "INT32"), // 144
        ("TargetId", "INT32"), // 145
        ("Md5", "GUID"), // 146
        ("GameVariable", "FLOAT32"), // 147
        ("GameVariableName", "CHAR"), // 148
        ("TimeMs", "INT32"), // 149
        ("SnapShotName", "CHAR"), // 150
        ("AisacControlId", "INT32"), // 151
        ("StartTimeMs", "INT64"), // 152
        ("SelectorName", "CHAR"), // 153
        ("LabelName", "CHAR"), // 154
        ("BlockName", "CHAR"), // 155
        ("CategoryName", "CHAR"), // 156
        ("AisacControlName", "CHAR"), // 157
        ("SettingName", "CHAR"), // 158
        ("CueSheetId", "INT32"), // 159
        ("BusNo", "INT8"), // 160
        ("FxType", "INT32"), // 161
        ("RemainedLoopCount", "INT32"), // 162
        ("SequenceLoopId", "INT16"), // 163
        ("3dPosVector_Position", "VECTOR"), // 164
        ("3dPosVector_Velocity", "VECTOR"), // 165
        ("3dPosVector_Forward", "VECTOR"), // 166
        ("3dPosVector_Upward", "VECTOR"), // 167
        ("3dPosVector_FocusPoint", "VECTOR"), // 168
        ("3dPosVector_Cone", "VECTOR"), // 169
        ("3dMaxAngleAisacDelta", "FLOAT32"), // 170
        ("3dEnablePriorityDecay", "INT32"), // 171
        ("3dDistanceFactor", "FLOAT32"), // 172
        ("3dDistanceFocusLevel", "FLOAT32"), // 173
        ("3dDirectionFocusLevel", "FLOAT32"), // 174
        ("Result3dPos", "INT8"), // 175
        ("CriAtomExAiffVoicePoolConfig", "UINTPTR"), // 176
        ("SoundRendererTyoe", "INT32"), // 177
        ("CriAtomExAt9VoicePoolConfig_PS4", "UINTPTR"), // 178
        ("AverageServerTime", "INT32"), // 179
        ("AverageServerInterval", "INT32"), // 180
        ("MaxServerTime", "INT32"), // 181
        ("MaxServerInterval", "INT32"), // 182
        ("UserLog", "CHAR"), // 183
        ("ByVoiceGroupLimitation", "INT8"), // 184
        ("ByVoicePoolLimitation", "INT8"), // 185
        ("RetryFlag", "INT8"), // 186
        ("BusName", "CHAR"), // 187
        ("StreamType", "INT8"), // 188
        ("MomentaryValue", "FLOAT32"), // 189
        ("ShortTermValue", "FLOAT32"), // 190
        ("IntegratedValue", "FLOAT32"), // 191
        ("TotalBps", "FLOAT32"), // 192
        ("num_cues", "INT32"), // 193
        ("SoundFormat", "INT32"), // 194
        ("CriAtomExAdpcmVoicePoolConfig_WIIU", "UINTPTR"), // 195
        ("AwbName", "CHAR"), // 196
        ("NumStreamAwb", "INT32"), // 197
        ("ExPlayback_AllocateModule", "INT8"), // 198
        ("AisacControlValue", "FLOAT32"), // 199
        ("NumAllPlaybacksForReact", "INT16"), // 200
        ("PreviewContext", "INT32"), // 201
        ("max_parameter_blocks", "INT16"), // 202
        ("max_faders", "INT16"), // 203
        ("num_buses", "INT16"), // 204
        ("max_racks", "INT16"), // 205
        ("output_channels_4_hcamx", "INT8"), // 206
        ("output_sampling_rate_4_hcamx", "INT32"), // 207
        ("sound_renderer_type_4_hcamx", "INT8"), // 208
        ("speaker_system", "INT8"), // 209
        ("left_speaker_angle", "FLOAT32"), // 210
        ("right_speaker_angle", "FLOAT32"), // 211
        ("center_speaker_angle", "FLOAT32"), // 212
        ("lfe_speaker_angle", "FLOAT32"), // 213
        ("surround_left_speaker_angle", "FLOAT32"), // 214
        ("surround_right_speaker_angle", "FLOAT32"), // 215
        ("surround_back_left_speaker_angle", "FLOAT32"), // 216
        ("surround_back_right_speaker_angle", "FLOAT32"), // 217
        ("pan_speaker_type", "INT8"), // 218
        ("VoiceStopReason", "INT16"), // 219
        ("CriAtomExVibrationVoicePoolConfig", "UINTPTR"), // 220
        ("TouceSenceEffectName", "CHAR"), // 221
        ("dsp_name", "CHAR"), // 222
        ("dsp_object", "UINTPTR"), // 223
        ("dsp_slot_no", "INT32"), // 224
        ("dsp_plugin_type", "INT32"), // 225
        ("3dPosVector_ListenerTop", "VECTOR"), // 226
        ("playback_status", "INT32"), // 227
        ("instrument_instance_callback", "UINTPTR"), // 228
        ("instrument_instance_callback_obj", "UINTPTR"), // 229
        ("instrument_instance_attach_player", "UINTPTR"), // 230
        ("instrument_instance", "UINTPTR"), // 231
        ("CriAtomEx3dTransceiverHn", "UINTPTR"), // 232
        ("CriAtomEx3dTransceiverConfig", "UINTPTR"), // 233
        ("CriAtomEx3dRegionHn", "UINTPTR"), // 234
        ("CriAtomEx3dTransceiverDirectAudioRadius", "FLOAT32"), // 235
        ("CriAtomEx3dTransceiverCrossFadeDistance", "FLOAT32"), // 236
        ("program_no", "INT32"), // 237
        ("key_no", "INT32"), // 238
        ("Velocity", "INT16"), // 239
        ("PitchBend", "INT16"), // 240
        ("Format", "INT32"), // 241
        ("MaxRhythmTracks", "INT16"), // 242
        ("MaxMelodyTracks", "INT16"), // 243
        ("MaxVocalTracks", "INT16"), // 244
        ("AdmPlayerHn", "UINTPTR"), // 245
        ("SpeakerMapping", "INT32"), // 246
        ("AmbisonicsOrderType", "INT32"), // 247
        ("MaxAisacs", "INT8"), // 248
        ("MaxBusSends", "INT8"), // 249
        ("RackId", "INT32"), // 250
        ("group_no", "INT32"), // 251
        ("priority", "INT32"), // 252
        ("Solo", "INT8"), // 253
        ("Param_Id", "INT32"), // 254
        ("Param_Name", "CHAR"), // 255
        ("Param_Index", "INT32"), // 256
        ("Param_Float32", "FLOAT32"), // 257
        ("Param_Sint32", "INT32"), // 258
        ("Param_Uint32", "INT32"), // 259
        ("BlockIndex", "INT32"), // 260
        ("PlaybackCancelType", "INT32"), // 261
        ("ModificationIgnoredReason", "INT32"), // 262
        ("ModifiedParam", "INT32"), // 263
        ("ModifiedId", "INT16"), // 264
        ("ModifiedName", "CHAR"), // 265
        ("ModifiedIndex", "INT16"), // 266
        ("CategoryId", "INT32"), // 267
        ("TweenParamType", "INT32"), // 268
        ("TweenParamId", "INT32"), // 269
        ("TweenAisacId", "INT32"), // 270
        ("TweenTime", "INT16"), // 271
        ("TweenValue", "FLOAT32"), // 272
        ("Parameter2Hn", "UINTPTR"), // 273
        ("LogId", "INT64"), // 274
        ("PacketVersion", "INT64"), // 275
        ("SelectorIndex", "INT16"), // 276
        ("LabelIndex", "INT16"), // 277
        ("GlobalAisacName", "CHAR"), // 278
        ("PanPitch", "FLOAT32"), // 279
        ("PanAngle", "FLOAT32"), // 280
        ("InteriorDistance", "FLOAT32"), // 281
        ("PanType", "INT32"), // 282
        ("PanSpeakerType", "INT32"), // 283
        ("PanAngleType", "INT32"), // 284
        ("PanSpread", "FLOAT32"), // 285
        ("BusSendLevel", "FLOAT32"), // 286
        ("LevelOffset", "FLOAT32"), // 287
        ("CofHigh", "FLOAT32"), // 288
        ("CofLow", "FLOAT32"), // 289
        ("BiquadFilterType", "INT32"), // 290
        ("Frequency", "FLOAT32"), // 291
        ("Gain", "FLOAT32"), // 292
        ("QValue", "FLOAT32"), // 293
        ("Ex3dSourceListHn", "UINTPTR"), // 294
        ("CurveType", "INT32"), // 295
        ("CurveStrength", "FLOAT32"), // 296
        ("EnvelopeSustainLevel", "FLOAT32"), // 297
        ("GlobalAisacIndex", "INT16"), // 298
        ("CuePriority", "INT32"), // 299
        ("DataRequestCallbackFunc", "UINTPTR"), // 300
        ("UserObject", "UINTPTR"), // 301
        ("RandomSeed", "INT32"), // 302
        ("BlockIndex", "INT32"), // 303
        ("ExPlayerCallback", "UINTPTR"), // 304
        ("ExPlaybackCallback", "UINTPTR"), // 305
        ("SamplingRate", "INT32"), // 306
        ("VoiceControlMethod", "INT32"), // 307
        ("HcaMxMixerId", "INT32"), // 308
        ("AsrRackIdArray", "INT32ARRAY"), // 309
        ("PlaybackRatio", "FLOAT32"), // 310
        ("LoopCount", "INT32"), // 311
        ("MixdownCenterVolumeOffset", "FLOAT32"), // 312
        ("MixdownLfeVolumeOffset", "FLOAT32"), // 313
        ("ExPanCallback", "UINTPTR"), // 314
        ("ChannelsPerTrack", "INT32ARRAY"), // 315
        ("SilentMode", "INT32"), // 316
        ("ExFilterCallback", "UINTPTR"), // 317
        ("BypassFlag", "INT8"), // 318
        ("BlockTransitionCallback", "UINTPTR"), // 319
        ("SpeakerId", "INT32"), // 320
        ("SendLevelOffset", "FLOAT32"), // 321
        ("SendLevelGain", "FLOAT32"), // 322
        ("ExPlayerPlaybackTrackInfoNotificationCallback", "UINTPTR"), // 323
        ("ExPlaybackEventCallback", "UINTPTR"), // 324
        ("ChannelConfig", "INT32"), // 325
        ("InsideAngle", "FLOAT32"), // 326
        ("OutsideAngle", "FLOAT32"), // 327
        ("OutsideVolume", "FLOAT32"), // 328
        ("MinAttenuationDistance", "FLOAT32"), // 329
        ("MaxAttenuationDistance", "FLOAT32"), // 330
        ("SourceRadius", "FLOAT32"), // 331
        ("InteriorDistance", "FLOAT32"), // 332
        ("DopplerFactor", "FLOAT32"), // 333
        ("MaxAngleAisacDelta", "FLOAT32"), // 334
        ("Ex3dSourceRandomPositionConfig", "UINTPTR"), // 335
        ("Ex3dSourceRandomPositionCalculationCallback", "UINTPTR"), // 336
        ("ExVectorArray", "VECTORARRAY"), // 337
        ("Ex3dSourceRandomPositionResult", "UINTPTR"), // 338
        ("DopplerMultiplier", "FLOAT32"), // 339
        ("Ex3dRegionConfig", "UINTPTR"), // 340
        ("TransceiverRadius", "FLOAT32"), // 341
        ("DspAfxConfig", "UINTPTR"), // 342
        ("DspPitchShifterConfig", "UINTPTR"), // 343
        ("DspTimeStretchConfig", "UINTPTR"), // 344
        ("ExCueLiniCallback", "UINTPTR"), // 345
        ("AngleArray", "FLOAT32ARRAY"), // 346
        ("ExPlaybackCancel", "UINTPTR"), // 347
        ("ErrorLevel", "INT32"), // 348
        ("ExTrackTransitionBySelectorCallback", "UINTPTR"), // 349
        ("ExVoiceEventCallback", "UINTPTR"), // 350
        ("ExVoiceInfoCallback", "UINTPTR"), // 351
        ("ExMonitoringVoiceStopCallback", "UINTPTR"), // 352
        ("MixerId", "INT32"), // 353
        ("FrequencyRatio", "FLOAT32"), // 354
        ("ExAcbHandleCallback", "UINTPTR"), // 355
        ("ExAcbDetectionInGamePreviewDataCallback", "UINTPTR"), // 356
        ("ExSequencerEventCallback", "UINTPTR"), // 357
        ("ExBeatSyncCallback", "UINTPTR"), // 358
        ("ExStreamingCacheId", "UINTPTR"), // 359
        ("Ex3dSoundObjectConfig", "UINTPTR"), // 360
        ("EnableVoiceLimitScope", "INT8"), // 361
        ("EnableCategoryCueLimitScope", "INT8"), // 362
        ("SoundObjectHn", "UINTPTR"), // 363
        ("PanInfoAngle", "FLOAT32"), // 364
        ("PanInfoDistance", "FLOAT32"), // 365
        ("PanInfoSpead", "FLOAT32"), // 366
        ("PanInfoVolume", "FLOAT32"), // 367
        ("InpuChannels", "INT32"), // 368
        ("BusMatrix", "FLOAT32ARRAY"), // 369
        ("SendToBusName", "CHAR"), // 370
        ("SendLevel", "FLOAT32"), // 371
        ("ParameterValue", "FLOAT32"), // 372
        ("ExAsrBusAnalyzerInterval", "INT32"), // 373
        ("ExAsrBusAnalyzerPeakHoldTimeMs", "INT32"), // 374
        ("ExAsrBusPreFilterCallback", "UINTPTR"), // 375
        ("ExAsrBusPostFilterCallback", "UINTPTR"), // 376
        ("ExAsrAfxInterfaceWithVersionPtr", "UINTPTR"), // 377
        ("ExAsrRackConfig", "UINTPTR"), // 378
        ("DspSettingName", "CHAR"), // 379
        ("OutputRackId", "INT32"), // 380
        ("AltRackId", "INT32"), // 381
        ("NumSamples", "INT32"), // 382
        ("ExOutputPortHn", "UINTPTR"), // 383
        ("ExOutputPortName", "INT8"), // 384
        ("ExOutputPortConfig", "UINTPTR"), // 385
        ("ExOutputPortType", "INT32"), // 386
        ("MaxIgnoredCategories", "INT32"), // 387
        ("Channel", "INT32"), // 388
        ("ChannelLevel", "FLOAT32"), // 389
        ("SpatializerType", "INT32"), // 390
        ("ExAsrSpatializerInterfacePtr", "UINTPTR"), // 391
        ("ExAsrSpatializerInitializeConfigPtr", "UINTPTR"), // 392
        ("SendToBusNo", "INT32"), // 393
        ("Ex3dSourceRandomPositionResultCallback", "UINTPTR"), // 394
        ("DirectAuiodRadius", "FLOAT32"), // 395
        ("CrossfadeDistance", "FLOAT32"), // 396
        ("PreviewRackType", "INT32"), // 397
        ("Flag", "INT8"), // 398
        ("ReactName", "CHAR"), // 399
        ("EnableDecrementAisacModulationKey", "INT8"), // 400
        ("DecrementAisacModulationKey", "INT32"), // 401
        ("EnableIncrementAisacModulationKey", "INT8"), // 402
        ("IncrementAisacModulationKey", "INT32"), // 403
        ("DuckerLevel", "FLOAT32"), // 404
        ("DuckerTargetType", "INT32"), // 405
        ("FadeParameterEntryCurveType", "INT32"), // 406
        ("FadeParameterEntryCurveStrength", "FLOAT32"), // 407
        ("FadeParameterEntryFadeTimeMs", "INT16"), // 408
        ("FadeParameterExitCurveType", "INT32"), // 409
        ("FadeParameterExitCurveStrength", "FLOAT32"), // 410
        ("FadeParameterExitFadeTimeMs", "INT16"), // 411
        ("HoldType", "INT32"), // 412
        ("HoldTimeMs", "INT16"), // 413
        ("HoldTime", "INT32"), // 414
        ("EnablePausingCue", "INT8"), // 415
        ("InterfaceName", "CHAR"), // 416
        ("InstrumentName", "CHAR"), // 417
        ("ExInstrumentVoicePoolConfig", "UINTPTR"), // 418
        ("ExAcbReleasedCallback", "UINTPTR"), // 419
        ("AtomPlayerCallback", "UINTPTR"), // 420
        ("EnableAtomSoundDisabledMode", "INT8"), // 421
        ("EnableAutoMatchingInPanTypeAuto", "INT8"), // 422
        ("EnableCategoryOverrideByExPlayer", "INT8"), // 423
        ("SequencePrepareRatio", "FLOAT32"), // 424
        ("FsThreadModel", "INT32"), // 425
        ("NumBinders", "INT32"), // 426
        ("NumGroupLoaders", "INT32"), // 427
        ("NumStdioHandles", "INT32"), // 428
        ("NumInstallers", "INT32"), // 429
        ("MaxBinds", "INT32"), // 430
        ("FsVersion", "INT32"), // 431
        ("FsVersionString", "CHAR"), // 432
        ("EnableCrcCheck", "INT8"), // 433
        ("ExAcfLocationInfoType", "INT32"), // 434
        ("Version", "INT32"), // 435
        ("VersionEx", "INT32"), // 436
        ("VersionString", "CHAR"), // 437
        ("VersionExString", "CHAR"), // 438
        ("ExResourceType", "INT32"), // 439
        ("BusIndex", "INT32"), // 440
        ("DistanceFactor", "FLOAT32"), // 441
        ("ConeOrientation", "VECTOR"), // 442
        ("IntervalMs", "INT32"), // 443
        ("HoldTimeMsSint32", "INT32"), // 444
        ("ShortTermTimeMs", "INT32"), // 445
        ("IntegratedTimeMs", "INT32"), // 446
        ("SampleClipping", "INT8"), // 447
        ("Marker_AtomExConfig", "INT8"), // 448
        ("Marker_AsrConfig", "INT8"), // 449
        ("Marker_HcaMxConfig", "INT8"), // 450
        ("DspID", "INT32"), // 451
        ("FollowsOriginalSource", "INT8"), // 452
        ("Ex3dSourceRandomPositionCalculationType", "INT32"), // 453
        ("Ex3dSourceRandomPositionCalculationParameters", "FLOAT32ARRAY"), // 454
        ("RandomPositionListMaxLength", "INT32"), // 455
        ("ExPlaybackId_unique64", "INT64"), // 456
        ("cause ExPlaybackId_unique64", "INT64"), // 457
        ("parent CriAtomExPlaybackId_unique64", "INT64"), // 458
        ("CriAtomSoundVoiceId_unique64", "INT64"), // 459
        ("TimeMsFloat", "FLOAT32"), // 460
        ("TimeMsInt16", "INT16"), // 461
        ("MaxIgnoredCategories", "INT32"), // 462
        ("NumDsp", "INT32"), // 463
        ("ConfigParameters", "FLOAT32ARRAY"), // 464
        ("DefaultParameters", "FLOAT32ARRAY"), // 465
        ("AfxInterfacePtr", "UINTPTR"), // 466
        ("PitchShiftMode", "INT32"), // 467
        ("WindowSize", "INT32"), // 468
        ("OverlapTimes", "INT32"), // 469
        ("PanInfoWideness", "FLOAT32"), // 470
        ("EnableAudioSyncedTimer", "INT32"), // 471
        ("VoiceAllocationMethod", "INT32"), // 472
        ("ACBBinderHn", "UINTPTR"), // 473
        ("AWBBinderHn", "UINTPTR"), // 474
        ("ExMp3VoicePoolConfig_PS5", "UINTPTR"), // 475
        ("ExMp3VoicePoolConfig_PS4", "UINTPTR"), // 476
        ("NumUsedPlayers", "INT32"), // 477
    ];
    internal static readonly Dictionary<ushort,string> Functions = new()
    {
        [2000] = "Non",
        [2001] = "Ex_Initialize",
        [2002] = "Ex_Finalize",
        [2003] = "ExAsr_Initialize",
        [2004] = "ExAsr_Finalize",
        [2005] = "ExHcaMx_Initialize",
        [2006] = "ExHcaMx_Finalize",
        [2007] = "Dbas_Create",
        [2008] = "Dbas_Destroy",
        [2009] = "StreamingCache_Create",
        [2010] = "StreamingCache_Destroy",
        [2011] = "ExVoicePool_AllocateStandardVoicePool",
        [2012] = "ExVoicePool_AllocateAdxVoicePool",
        [2013] = "ExVoicePool_AllocateAhxVoicePool",
        [2014] = "ExVoicePool_AllocateHcaVoicePool",
        [2015] = "ExVoicePool_AllocateHcaMxVoicePool",
        [2016] = "ExVoicePool_AllocateWaveVoicePool",
        [2017] = "ExVoicePool_AllocateRawPcmVoicePool",
        [2018] = "ExVoicePool_AllocateAdpcmVoicePool_WII",
        [2019] = "ExVoicePool_AllocateVagVoicePool_PSP",
        [2020] = "ExVoicePool_AllocateAdpcmVoicePool_3DS",
        [2021] = "ExVoicePool_AllocateVagVoicePool_VITA",
        [2022] = "ExVoicePool_AllocateAtrac3VoicePool_PSP",
        [2023] = "ExVoicePool_AllocateAt9VoicePool_VITA",
        [2024] = "ExVoicePool_Free",
        [2025] = "ExPlayer_Create",
        [2026] = "ExPlayer_Destroy",
        [2027] = "ExTween_Create",
        [2028] = "ExTween_Destroy",
        [2029] = "Decrypter_Create",
        [2030] = "Decrypter_Destroy",
        [2031] = "Ex3dSource_Create",
        [2032] = "Ex3dSource_Destroy",
        [2033] = "Ex3dListener_Create",
        [2034] = "Ex3dListener_Destroy",
        [2035] = "ExPlayer_AttachFader",
        [2036] = "ExPlayer_DetachFader",
        [2037] = "Ex_RegisterAcfConfig",
        [2038] = "Ex_RegisterAcfData",
        [2039] = "Ex_RegisterAcfFile",
        [2040] = "Ex_RegisterAcfFileById",
        [2041] = "Ex_UnregisterAcf",
        [2042] = "ExAcb_LoadAcbData",
        [2043] = "ExAcb_LoadAcbDataById",
        [2044] = "ExAcb_LoadAcbFile",
        [2045] = "ExAcb_LoadAcbFileById",
        [2046] = "ExAcb_Release",
        [2047] = "ExAcb_ReleaseAll",
        [2048] = "ExPlayer_Start",
        [2049] = "ExPlayer_Prepare",
        [2050] = "ExPlayer_Stop",
        [2051] = "ExPlayer_StopWithoutReleaseTime",
        [2052] = "ExPlayback_Stop",
        [2053] = "ExPlayback_StopWithoutReleaseTime",
        [2054] = "ExPlayer_Pause",
        [2055] = "ExPlayer_Resume",
        [2056] = "ExPlayback_Pause",
        [2057] = "ExPlayback_Resume",
        [2058] = "ExPlaybackInfo_AllocateInfo",
        [2059] = "ExPlaybackInfo_FreeInfo",
        [2060] = "Error",
        [2061] = "ExPlaybackInfo_AddSound",
        [2062] = "ExPlaybackSound_FreeSound",
        [2063] = "SoundPlayer_Start",
        [2064] = "SoundPlayer_Stop",
        [2065] = "SoundPlayer_StopWithoutRelease",
        [2066] = "SoundPlayer_PausePlayback",
        [2067] = "SoundPlayer_SetWaveId",
        [2068] = "SoundPlayer_SetContentId",
        [2069] = "SoundPlayer_SetData",
        [2070] = "SoundPlayer_SetFileStringPointer",
        [2071] = "ExPlayer_SetCueId",
        [2072] = "ExPlayer_SetCueName",
        [2073] = "ExPlayer_SetCueIndex",
        [2074] = "ExPlayer_SetData",
        [2075] = "ExPlayer_SetFile",
        [2076] = "ExPlayer_SetContentId",
        [2077] = "ExPlayer_SetWaveId",
        [2078] = "DbasId",
        [2079] = "StreamingCacheId",
        [2080] = "ExVoicePoolHn",
        [2081] = "ExPlayerHn",
        [2082] = "ExTweenHn",
        [2083] = "DecrypterHn",
        [2084] = "Ex3dSourceHn",
        [2085] = "Ex3dListenerHn",
        [2086] = "ExPlaybackId",
        [2087] = "ExConfig",
        [2088] = "ExAsrConfig",
        [2089] = "ExHcaMxConfig",
        [2090] = "DbasConfig",
        [2091] = "StreamingCacheConfig",
        [2092] = "ExStandardVoicePoolConfig",
        [2093] = "ExAdxVoicePoolConfig",
        [2094] = "ExAhxVoicePoolConfig",
        [2095] = "ExHcaVoicePoolConfig",
        [2096] = "ExHcaMxVoicePoolConfig",
        [2097] = "ExWaveVoicePoolConfig",
        [2098] = "ExRawPcmVoicePoolConfig",
        [2099] = "ExAdpcmVoicePoolConfig_3DS",
        [2100] = "ExAdpcmVoicePoolConfig_WII",
        [2101] = "ExVagVoicePoolConfig_PSP",
        [2102] = "ExAtrac3VoicePoolConfig_PSP",
        [2103] = "ExVagVoicePoolConfig_VITA",
        [2104] = "ExAt9VoicePoolConfig_VITA",
        [2105] = "ExPlayerConfig",
        [2106] = "ExTweenConfig",
        [2107] = "DecrypterConfig",
        [2108] = "ExAcfConfig",
        [2109] = "Ex3dSourceConfig",
        [2110] = "Ex3dListenerConfig",
        [2111] = "ExFaderConfig",
        [2112] = "ExAcbHn",
        [2113] = "ExFaderHn",
        [2114] = "PlayerPoolPlayerInfo",
        [2115] = "PlayerPool_ReleasePlayer",
        [2116] = "SoundPlayer_Allocate_Element",
        [2117] = "ExCategory_CancelCuePlayback",
        [2118] = "ExCue_StopByLimit",
        [2119] = "ExCue_CancelCuePlayback",
        [2120] = "CancelPlaybackByProbability",
        [2121] = "CancelPlaybackByCueTypeRandom",
        [2122] = "CancelPlaybackByCueTypeSwitch",
        [2123] = "ExCategory_IncrementNumPlaybackCues",
        [2124] = "ExCategory_DecrementNumPlaybackCues",
        [2125] = "SoundVoice_Volume",
        [2126] = "SoundVoice_FreeVoice",
        [2127] = "ExSequence_GetFreeBlock",
        [2128] = "ExSequence_SetFreeBlock",
        [2129] = "ExSequence_GetFreeSeqInfo",
        [2130] = "ExSequence_SetFreeSeqInfo",
        [2131] = "SoundVoice_Allocate",
        [2132] = "GetAisacDestinationValue",
        [2133] = "SequenceTrack_Mute",
        [2134] = "Preview_RequestSendLog_DELETED",
        [2135] = "RequestSendAcb",
        [2136] = "Monitor_MakeClosePacket",
        [2137] = "Monitor_MakeSendDataResultPacket",
        [2138] = "CpuLoadAndNumUsedVoices",
        [2139] = "SequenceCallback",
        [2140] = "OverwriteAcf",
        [2141] = "StartLogging",
        [2142] = "SequenceLoopInfo",
        [2143] = "Ex3dSource_Update",
        [2144] = "Ex3dListener_Update",
        [2145] = "ExVoicePool_AllocateAiffVoicePool",
        [2146] = "ExAiffVoicePoolConfig",
        [2147] = "ExVoicePool_AllocateAt9VoicePool_PS4",
        [2148] = "ExAt9VoicePoolConfig_PS4",
        [2149] = "UserLog",
        [2150] = "ExCategoryConfig",
        [2151] = "SoundVoice_KillByLimit",
        [2152] = "SoundVoice_Virtualize",
        [2153] = "SoundVoice_UnVirtualize",
        [2154] = "AsrBusAnalyzeInfo",
        [2155] = "LoudnessInfo",
        [2156] = "StreamingInfo",
        [2157] = "PlayerPool_NumVoices",
        [2158] = "ExVoicePool_AllocateAdpcmVoicePool_WIIU",
        [2159] = "ExAdpcmVoicePoolConfig_WIIU",
        [2160] = "StreamTypeMemory",
        [2161] = "StreamTypeStream",
        [2162] = "StreamTypeZeroLatencyStream",
        [2163] = "AsrBusAnalyzeInfoAllCh",
        [2164] = "ExPlayer_SetAisacControlValue",
        [2165] = "SequenceTrack_Start",
        [2166] = "SequenceTrack_Stop",
        [2167] = "SoundPlayer_SetVibrationId",
        [2168] = "ExGameVariableConfig",
        [2169] = "SetGameVariable",
        [2170] = "ExVibrationVoicePoolConfig",
        [2171] = "ExVoicePool_AllocateVibrationVoicePool",
        [2172] = "ExVoicePlayer_Pause",
        [2173] = "SoundPlayer_SetVibrationName",
        [2174] = "ExCategory_Stop",
        [2175] = "ExCategory_StopWithoutReleaseTime",
        [2176] = "Ex3dTransceiver_Create",
        [2177] = "Ex3dTransceiver_Destroy",
        [2178] = "Ex3dTransceiverHn",
        [2179] = "Ex3dTransceiverConfig",
        [2180] = "Ex3dTransceiver_UpdateInput",
        [2181] = "Ex3dTransceiver_UpdateOutput",
        [2182] = "SoundVoice_CalcFinalVoiceParamAndSilentVolumeCore",
        [2183] = "AdmPlayer_Create",
        [2184] = "AdmPlayer_Destroy",
        [2185] = "AdmPlayer_InternalStart",
        [2186] = "AdmPlayer_Start",
        [2187] = "AdmPlayer_Stop",
        [2188] = "AdmPlayer_StopByPhraseEnd",
        [2189] = "AsrRack_CreateForAcfLinkage",
        [2190] = "AsrRack_DestroyForAcfLinkage",
        [2191] = "LoudnessInfoWithRackId",
        [2192] = "AsrBusAnalyzeInfoAllChWithRackId",
        [2193] = "AsrRackConfig",
        [2194] = "AsrRack_Create",
        [2195] = "AsrRack_Destroy",
        [2196] = "SoundVoice_KillByLimit_2",
        [2197] = "ExPlayer_Set3dSourceHn",
        [2198] = "ExPlayer_Set3dListenerHn",
        [2199] = "ExPlayer_UpdateAll",
        [2200] = "ExCategory_MuteByName",
        [2201] = "ExCategory_SoloByName",
        [2202] = "ExPlayer_SetParameterFloat32",
        [2203] = "ExPlayer_SetParameterSint32",
        [2204] = "ExPlayer_SetParameterUint32",
        [2205] = "ExPlayback_SetNextBlockIndex",
        [2206] = "CancelPlaybackCouldNotSetupTrack",
        [2207] = "ChangesIgnored",
        [2208] = "ExCategory_MuteById",
        [2209] = "ExCategory_SoloById",
        [2210] = "ExCategory_SetVolumeById",
        [2211] = "ExCategory_SetVolumeByName",
        [2212] = "ExPlayer_AttachTween",
        [2213] = "ExTween_MoveTo",
        [2214] = "ExTween_MoveFrom",
        [2215] = "ExTween_Stop",
        [2216] = "ExTween_Reset",
        [2217] = "ExPlayer_DetachTween",
        [2218] = "Ex_AttachDspBusSetting",
        [2219] = "Ex_DetachDspBusSetting",
        [2220] = "Ex_ApplyDspBusSnapshot",
        [2221] = "ExCategory_SetAisacControlById",
        [2222] = "ExCategory_SetAisacControlByName",
        [2223] = "ExAcf_SetGlobalLabelToSelectorByName",
        [2224] = "ExAcf_SetGlobalLabelToSelectorByIndex",
        [2225] = "ExCategory_SetFadeInTimeById",
        [2226] = "ExCategory_SetFadeInTimeByName",
        [2227] = "ExCategory_SetFadeOutTimeById",
        [2228] = "ExCategory_SetFadeOutTimeByName",
        [2229] = "ExCategory_AttachAisacById",
        [2230] = "ExCategory_AttachAisacByName",
        [2231] = "ExCategory_DetachAisacById",
        [2232] = "ExCategory_DetachAisacByName",
        [2233] = "ExCategory_DetachAisacAllById",
        [2234] = "ExCategory_DetachAisacAllByName",
        [2235] = "ExPlayer_StopAllPlayers",
        [2236] = "ExPlayer_StopAllPlayersWithoutReleaseTime",
        [2237] = "ExPlayer_SetVoicePoolIdentifier",
        [2238] = "ExPlayer_SetAsrRackId",
        [2239] = "ExPlayer_SetStartTime",
        [2240] = "ExPlayer_Update",
        [2241] = "ExPlayer_ResetParameters",
        [2242] = "ExPlayer_SetVolume",
        [2243] = "ExPlayer_SetPitch",
        [2244] = "ExPlayer_SetMaxPitch",
        [2245] = "ExPlayer_SetPan3dAngle",
        [2246] = "ExPlayer_SetPan3dInteriorDistance",
        [2247] = "ExPlayer_SetPan3dVolume",
        [2248] = "ExPlayer_SetPanType",
        [2249] = "ExPlayer_SetPanSpeakerType",
        [2250] = "ExPlayer_SetPanAngleType",
        [2251] = "ExPlayer_SetPanSpread",
        [2252] = "ExPlayer_SetSendLevel",
        [2253] = "ExPlayer_SetBusSendLevelByName",
        [2254] = "ExPlayer_ResetBusSends",
        [2255] = "ExPlayer_SetBusSendLevelOffsetByName",
        [2256] = "ExPlayer_SetBandpassFilterParameters",
        [2257] = "ExPlayer_SetBiquadFilterParameters",
        [2258] = "ExPlayer_ClearAisacControls",
        [2259] = "ExPlayer_SetVoicePriority",
        [2260] = "ExPlayer_Set3dSourceListHn",
        [2261] = "ExPlayer_SetEnvelopeAttackTime",
        [2262] = "ExPlayer_SetEnvelopeAttackCurve",
        [2263] = "ExPlayer_SetEnvelopeHoldTime",
        [2264] = "ExPlayer_SetEnvelopeDecayTime",
        [2265] = "ExPlayer_SetEnvelopeDecayCurve",
        [2266] = "ExPlayer_SetEnvelopeReleaseTime",
        [2267] = "ExPlayer_SetEnvelopeReleaseCurve",
        [2268] = "ExPlayer_SetEnvelopeSustainLevel",
        [2269] = "ExPlayer_AttachAisac",
        [2270] = "ExPlayer_AttachAisacByIndex",
        [2271] = "ExPlayer_DetachAisac",
        [2272] = "ExPlayer_DetachAisacByIndex",
        [2273] = "ExPlayer_DetachAisacAll",
        [2274] = "ExPlayer_UnsetCategory",
        [2275] = "ExPlayer_SetCuePriority",
        [2276] = "ExPlayer_SetPreDelayTime",
        [2277] = "ExPlayer_SetDataRequestCallback",
        [2278] = "ExPlayer_SetStreamingCacheId",
        [2279] = "ExPlayer_SetRandomSeed",
        [2280] = "ExPlayer_SetFirstBlockIndex",
        [2281] = "ExPlayer_SetSelectorLabel",
        [2282] = "ExPlayer_UnsetSelectorLabel",
        [2283] = "ExPlayer_ClearAllSelectorLabels",
        [2284] = "ExPlayer_SetFadeInStartOffset",
        [2285] = "ExPlayer_SetFadeInTime",
        [2286] = "ExPlayer_SetFadeOutTime",
        [2287] = "ExPlayer_SetFadeOutEndDelay",
        [2288] = "ExPlayer_ResetFaderParameters",
        [2289] = "ExPlayer_EnumeratePlayers",
        [2290] = "ExPlayer_EnumeratePlaybacks",
        [2291] = "ExPlayer_SetFormat",
        [2292] = "ExPlayer_SetNumChannels",
        [2293] = "ExPlayer_SetSamplingRate",
        [2294] = "ExPlayer_SetSoundRendererType",
        [2295] = "ExPlayer_SetGroupNumber",
        [2296] = "ExPlayer_SetVoiceControlMethod",
        [2297] = "ExPlayer_SetHcaMxMixerId",
        [2298] = "ExPlayer_SetAsrRackIdArray",
        [2299] = "ExPlayer_SetSyncPlaybackId",
        [2300] = "ExPlayer_SetPlaybackRatio",
        [2301] = "ExPlayer_LimitLoopCount",
        [2302] = "ExPlayer_AddMixDownCenterVolumeOffset",
        [2303] = "ExPlayer_AddMixDownLfeVolumeOffset",
        [2304] = "ExPlayer_ChangeDefaultPanSpeakerType",
        [2305] = "ExPlayer_OverrideDefaultPanMethod",
        [2306] = "ExPlayer_SetTrackInfo",
        [2307] = "ExPlayer_SetTrackVolume",
        [2308] = "ExPlayer_SetSilentMode",
        [2309] = "ExPlayer_SetFilterCallback",
        [2310] = "ExPlayer_SetDspParameter",
        [2311] = "ExPlayer_SetDspBypass",
        [2312] = "ExPlayer_SetBlockTransitionCallback",
        [2313] = "ExPlayer_SetDrySendLevel",
        [2314] = "ExPlayer_SetPlaybackTrackInfoNotificationCallback",
        [2315] = "ExPlayer_SetPlaybackEventCallback",
        [2316] = "ExPlayer_SetChannelConfig",
        [2317] = "Ex3dSource_ResetParameters",
        [2318] = "Ex3dSource_SetPosition",
        [2319] = "Ex3dSource_SetVelocity",
        [2320] = "Ex3dSource_SetOrientation",
        [2321] = "Ex3dSource_SetConeParameter",
        [2322] = "Ex3dSource_ChangeDefaultConeParameter",
        [2323] = "Ex3dSource_SetMinMaxAttenuationDistance",
        [2324] = "Ex3dSource_ChangeDefaultMinMaxAttenuationDistance",
        [2325] = "Ex3dSource_SetInteriorPanField",
        [2326] = "Ex3dSource_ChangeDefaultInteriorPanField",
        [2327] = "Ex3dSource_SetDopplerFactor",
        [2328] = "Ex3dSource_ChangeDefaultDopplerFactor",
        [2329] = "Ex3dSource_SetVolume",
        [2330] = "Ex3dSource_ChangeDefaultVolume",
        [2331] = "Ex3dSource_SetMaxAngleAisacDelta",
        [2332] = "Ex3dSource_SetDistanceAisacControlId",
        [2333] = "Ex3dSource_SetListenerBasedAzimuthAngleAisacControlId",
        [2334] = "Ex3dSource_SetListenerBasedElevationAngleAisacControlId",
        [2335] = "Ex3dSource_SetSourceBasedAzimuthAngleAisacControlId",
        [2336] = "Ex3dSource_SetSourceBasedElevationAngleAisacControlId",
        [2337] = "Ex3dSource_Set3dRegionHn",
        [2338] = "Ex3dSource_SetRandomPositionConfig",
        [2339] = "Ex3dSource_SetRandomPositionCalculationCallback",
        [2340] = "Ex3dSource_SetRandomPositionList",
        [2341] = "Ex3dSource_SetRandomPositionResultCallback",
        [2342] = "Ex3dSourceList_Create",
        [2343] = "Ex3dSourceList_Destroy",
        [2344] = "Ex3dSourceList_Add",
        [2345] = "Ex3dSourceList_Remove",
        [2346] = "Ex3dSourceList_RemoveAll",
        [2347] = "Ex3dListener_ResetParameters",
        [2348] = "Ex3dListener_SetPosition",
        [2349] = "Ex3dListener_SetVelocity",
        [2350] = "Ex3dListener_SetOrientation",
        [2351] = "Ex3dListener_SetDopplerMultiplier",
        [2352] = "Ex3dListener_SetFocusPoint",
        [2353] = "Ex3dListener_SetDistanceFocusLevel",
        [2354] = "Ex3dListener_SetDirectionFocusLevel",
        [2355] = "Ex3dRegion_Create",
        [2356] = "Ex3dRegionConfig",
        [2357] = "Ex3dRegion_Destroy",
        [2358] = "Ex3dTransceiver_SetInputPosition",
        [2359] = "Ex3dTransceiver_SetOutputPosition",
        [2360] = "Ex3dTransceiver_SetInputOrientation",
        [2361] = "Ex3dTransceiver_SetOutputOrientation",
        [2362] = "Ex3dTransceiver_SetOutputConeParameter",
        [2363] = "Ex3dTransceiver_SetOutputMinMaxAttenuationDistance",
        [2364] = "Ex3dTransceiver_SetOutputInteriorPanField",
        [2365] = "Ex3dTransceiver_SetInputCrossFadeField",
        [2366] = "Ex3dTransceiver_SetOutputVolume",
        [2367] = "Ex3dTransceiver_AttachAisac",
        [2368] = "Ex3dTransceiver_DetachAisac",
        [2369] = "Ex3dTransceiver_SetMaxAngleAisacDelta",
        [2370] = "Ex3dTransceiver_SetDistanceAisacControlId",
        [2371] = "Ex3dTransceiver_SetListenerBasedAzimuthAngleAisacControlId",
        [2372] = "Ex3dTransceiver_SetListenerBasedElevationAngleAisacControlId",
        [2373] = "Ex3dTransceiver_SetTransceiverOutputBasedAzimuthAngleAisacControlId",
        [2374] = "Ex3dTransceiver_SetTransceiverOutputBasedElevationAngleAisacControlId",
        [2375] = "Ex3dTransceiver_Set3dRegionHn",
        [2376] = "ExVoicePool_AttachDspPitchShifter",
        [2377] = "ExVoicePool_AttachDspTimeStretch",
        [2378] = "ExVoicePool_AttachDspAfx",
        [2379] = "ExVoicePool_DetachDsp",
        [2380] = "Ex_SetRandomSeed",
        [2381] = "Ex_SetCueLinkCallback",
        [2382] = "Ex_SetSpeakerAngles",
        [2383] = "Ex_SetSpeakerAngleArray",
        [2384] = "Ex_SetVirtualSpeakerAngleArray",
        [2385] = "Ex_ControlVirtualSpeakerSetting",
        [2386] = "Ex_SetPlaybackCancelCallback",
        [2387] = "Ex_ControlAcfConsistencyCheck",
        [2388] = "Ex_SetAcfConsistencyCheckErrorLevel",
        [2389] = "Ex_SetTrackTransitionBySelectorCallback",
        [2390] = "Ex_EnableCalculationAisacControlFrom3dPosition",
        [2391] = "Ex_SetVoiceEventCallback",
        [2392] = "Ex_EnumerateVoiceInfos",
        [2393] = "Ex_SetMonitoringVoiceStopCallback",
        [2394] = "Ex_SetMonitoringVoiceStopPlaybackId",
        [2395] = "Ex_ResetTimer",
        [2396] = "Ex_PauseTimer",
        [2397] = "Ex_Lock",
        [2398] = "Ex_Unlock",
        [2399] = "ExHcaMx_SetBusSendLevelByName",
        [2400] = "ExHcaMx_SetFrequencyRatio",
        [2401] = "ExHcaMx_SetAsrRackId",
        [2402] = "ExAcb_EnumerateHandles",
        [2403] = "ExAcb_SetDetectionInGamePreviewDataCallback",
        [2404] = "ExAcb_ResetCueTypeStateByName",
        [2405] = "ExAcb_ResetCueTypeStateById",
        [2406] = "ExAcb_ResetCueTypeStateByIndex",
        [2407] = "ExPlayback_SetBeatSyncOffset",
        [2408] = "ExSequencer_SetEventCallback",
        [2409] = "ExBeatSync_SetCallback",
        [2410] = "ExStreamingCache_LoadWaveformByIdAsync",
        [2411] = "ExStreamingCache_LoadWaveformByNameAsync",
        [2412] = "ExStreamingCache_LoadWaveformById",
        [2413] = "ExStreamingCache_LoadWaveformByName",
        [2414] = "ExSoundObject_Create",
        [2415] = "ExSoundObjectConfig",
        [2416] = "ExSoundObject_Destroy",
        [2417] = "ExSoundObject_AddPlayer",
        [2418] = "ExSoundObject_DeletePlayer",
        [2419] = "ExSoundObject_DeleteAllPlayers",
        [2420] = "ExAsrRack_SetBusVolumeByName",
        [2421] = "ExAsrRack_SetBusPanInfoByName",
        [2422] = "ExAsrRack_SetBusMatrixByName",
        [2423] = "ExAsrRack_SetBusSendLevelByName",
        [2424] = "ExAsrRack_SetEffectParameter",
        [2425] = "ExAsrRack_UpdateEffectParameters",
        [2426] = "ExAsrRack_SetEffectBypass",
        [2427] = "ExAsrRack_AttachBusAnalyzerByName",
        [2428] = "ExAsrRack_DetachBusAnalyzerByName",
        [2429] = "ExAsrRack_SetBusFilterCallbackByName",
        [2430] = "ExAsr_RegisterEffectInterface",
        [2431] = "ExAsr_UnregisterEffectInterface",
        [2432] = "ExAsr_ResetIrReverbPerformanceInfo",
        [2433] = "ExAsrRack_Create",
        [2434] = "ExAsrRackConfig",
        [2435] = "ExAsrRack_Destroy",
        [2436] = "ExAsrRack_ResetPerformanceMonitor",
        [2437] = "ExAsrRack_AttachDspBusSetting",
        [2438] = "ExAsrRack_DetachDspBusSetting",
        [2439] = "ExAsrRack_ApplyDspBusSnapshot",
        [2440] = "ExAsrRack_SetAlternateRackId",
        [2441] = "ExAsr_SetPcmBufferSize",
        [2442] = "ExAsrRack_SetAisacControlById",
        [2443] = "ExAsrRack_SetAisacControlByName",
        [2444] = "ExAsr_EnableBinauralizer",
        [2445] = "ExPlayer_AddOutputPort",
        [2446] = "ExPlayer_RemoveOutputPort",
        [2447] = "ExPlayer_ClearOutputPorts",
        [2448] = "ExPlayer_AddPreferredOutputPort",
        [2449] = "ExPlayer_RemovePreferredOutputPort",
        [2450] = "ExPlayer_RemovePreferredOutputPortByName",
        [2451] = "ExPlayer_ClearPreferredOutputPorts",
        [2452] = "ExOutputPort_Create",
        [2453] = "ExOutputPortConfig",
        [2454] = "ExOutputPort_Destroy",
        [2455] = "ExOutputPort_SetAsrRackId",
        [2456] = "ExOutputPort_SetVibrationChannelLevel",
        [2457] = "ExOutputPort_SetMonauralMix",
        [2458] = "ExOutputPort_IgnoreCategoryParametersById",
        [2459] = "ExOutputPort_ResetIgnoreCategory",
        [2460] = "Asr_PauseOutputVoice",
        [2461] = "ExAsr_AddResource",
        [2462] = "ExAsr_RemoveResource",
        [2463] = "ExAsrRack_CreateForAdditionalResource",
        [2464] = "ExAsr_EnableSoundXr",
        [2465] = "ExAsr_RegisterSpatializerInterface",
        [2466] = "ExAsrRack_SetBusVolume",
        [2467] = "ExAsrRack_SetBusPan3d",
        [2468] = "ExAsrRack_SetBusPan3dByName",
        [2469] = "ExAsrRack_SetBusMatrix",
        [2470] = "ExAsrRack_SetBusSendLevel",
        [2471] = "ExAsrRack_AttachBusAnalyzer",
        [2472] = "ExAsrRack_DetachBusAnalyzer",
        [2473] = "ExAsrRack_SetBusFilterCallback",
        [2474] = "Ex_SetGameVariableById",
        [2475] = "Ex_SetGameVariableByName",
        [2476] = "ExAcb_AttachAwbFile",
        [2477] = "ExAcb_DetachAwbFile",
        [2478] = "ExVoicePool_AllocateHcaVoicePoolInternal",
        [2479] = "ExVoicePool_FreeAll",
        [2480] = "ExAsr_PauseOutputVoice",
        [2481] = "Ex_ExecuteMain",
        [2482] = "Ex_ExecuteAudioProcess",
        [2483] = "ExCategory_PauseById",
        [2484] = "ExCategory_PauseByName",
        [2485] = "ExCategory_ResetAllAisacControlById",
        [2486] = "ExCategory_ResetAllAisacControlByName",
        [2487] = "ExCategory_StopById",
        [2488] = "ExCategory_StopByName",
        [2489] = "ExCategory_StopWithoutReleaseTimeById",
        [2490] = "ExCategory_StopWithoutReleaseTimeByName",
        [2491] = "ExPlayer_SetCategoryById",
        [2492] = "ExPlayer_SetCategoryByName",
        [2493] = "ExPlayer_DetachTweenAll",
        [2494] = "Ex3dListener_Set3dRegionHn",
        [2495] = "ExPlayer_StartAsync",
        [2496] = "ExPlayer_SetNextBlockIndex",
        [2497] = "ExCategory_SetReactParameter",
        [2498] = "ExVoicePool_AllocateInstrumentVoicePool",
        [2499] = "ExVoicePool_InstrumentVoicePoolConfig",
        [2500] = "ExAcb_ReleaseAsync",
        [2501] = "ExPlayer_UpdateAllAsync",
        [2502] = "ExPlayback_EnumerateAtomPlayers",
        [2503] = "ExPlayback_EnumerateVoiceInfos",
        [2504] = "Ex3dSource_SetAttenuationDistanceSetting",
        [2505] = "Ex_AddResource",
        [2506] = "Ex_RemoveResource",
        [2507] = "ExPlayer_SetResourceType",
        [2508] = "ExPlayer_SetBusSendLevel",
        [2509] = "Ex3dListener_SetDistanceFactor",
        [2510] = "ExPlayer_SetBusSendLevelOffset",
        [2511] = "Ex3dSource_SetConeOrientation",
        [2512] = "ExAsr_SetConfigForWorkSizeCalculation",
        [2513] = "ExAsrRack_AttachLevelMeter",
        [2514] = "ExAsrRack_DetachLevelMeter",
        [2515] = "ExAsrRack_AttachLoudnessMeter",
        [2516] = "ExAsrRack_DetachLoudnessMeter",
        [2517] = "ExAsrRack_ResetLoudnessMeter",
        [2518] = "ExAsrRack_AttachTruePeakMeter",
        [2519] = "ExAsrRack_DetachTruePeakMeter",
        [2520] = "Asr_Initialize",
        [2521] = "Asr_Finalize",
        [2522] = "Ex_InitializeForUserPcmOutput",
        [2523] = "Ex_FinalizeForUserPcmOut",
        [2524] = "ExAsr_SetDspBypassByName",
        [2525] = "Ex3dSource_Create_Success",
        [2526] = "Ex3dListener_Create_Success",
        [2527] = "Ex3dRegion_Create_Success",
        [2528] = "Ex3dSourceList_Create_Success",
        [2529] = "Ex3dTransceiver_Create_Success",
        [2530] = "Ex_InitializeWASAPI",
        [2531] = "Ex_FinalizeWASAPI",
        [2532] = "Ex_InitializeCOMMON",
        [2533] = "Ex_FinalizeCOMMON",
        [2534] = "Ex_InitializeMACOSX",
        [2535] = "Ex_FinalizeMACOSX",
        [2536] = "ExPlayer_Create_Success",
        [2537] = "ExAcb_LoadAcbFile_Success",
        [2538] = "DoActionApplyMixerSnapshot",
        [2539] = "ExVoicePool_AllocateMp3VoicePool_PS5",
        [2540] = "ExVoicePool_AllocateMp3VoicePool_PS4",
    };
}
