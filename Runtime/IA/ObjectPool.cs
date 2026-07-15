using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class ObjectPool <T>
{
    Func<T> _factoryMethod;

    List<T> _currentStock;
    bool _isDynamic;

    Action<T> _TurnOnCallback;
    Action<T> _TurnOffCallback;

    public ObjectPool(Func<T> factoryMethod, Action<T> TurnOnCallback, Action<T> TurnOffCallback, int initialAmount, bool isDynamic = true)
    {
        _factoryMethod = factoryMethod;
        _TurnOnCallback = TurnOnCallback;
        _TurnOffCallback = TurnOffCallback;
        _isDynamic = isDynamic;
        _currentStock = new List<T>(initialAmount);

        for (int i = 0; i < initialAmount; i++)
        {
            T obj = _factoryMethod();

            _TurnOffCallback(obj);

            _currentStock.Add(obj);
        }
    }

    public T GetObject()
    {
        T ObjToGet = default(T);
        if (_currentStock.Count > 0)
        {
            ObjToGet = _currentStock[0];
            _currentStock.RemoveAt(0);
        }
        else if (_isDynamic)
        {
            ObjToGet = _factoryMethod();
        }

        _TurnOnCallback(ObjToGet);        
        return ObjToGet;
    }

    public void ReturnObject(T obj)
    {
        _TurnOffCallback(obj);
        _currentStock.Add(obj);
    }
}
