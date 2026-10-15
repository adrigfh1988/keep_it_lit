using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// F122: tells a menu which row the player is on, from the mouse (hover) or from keyboard / gamepad focus (select), so the screen can show that row's
/// description. Put on any Selectable of a runtime menu; <see cref="OnHint"/> runs on pointer-enter and on select.
/// </summary>
public class SelectHint : MonoBehaviour, ISelectHandler, IPointerEnterHandler
{
    public Action OnHint;

    public void OnSelect(BaseEventData eventData) => OnHint?.Invoke();

    public void OnPointerEnter(PointerEventData eventData) => OnHint?.Invoke();
}
