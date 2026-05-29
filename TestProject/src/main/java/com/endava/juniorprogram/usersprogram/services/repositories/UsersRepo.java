package com.endava.juniorprogram.usersprogram.services.repositories;

import com.endava.juniorprogram.usersprogram.model.User;
import org.springframework.stereotype.Repository;

import java.util.ArrayList;
import java.util.List;

@Repository
public class UsersRepo implements DatabaseRepository{
    @Override
    public List<User> getUsers(String name) {
        List<User> users = new ArrayList<>();
        if(name == null){
            users.add(new User(1L, "Diego"));
            users.add(new User(2L, "Carina"));
            users.add(new User(3L, "Elias"));
        }else{
            users.add(new User(4L, name));
        }
        return users;

    }

    @Override
    public User getUser(long id) {
        return new User(id, "Elias");
    }

    @Override
    public User deleteUser(long id) {
        return new User(id, "Elias");
    }

    @Override
    public User addUser(User user) {
        return new User(user.getId(), user.getName());
    }

    @Override
    public User updateUser(User user) {
        return new User(user.getId(), user.getName());
    }
}
